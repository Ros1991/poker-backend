using System.Diagnostics;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PokerTournament.Application.Ai;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Infrastructure.Ai;

/// <summary>
/// <see cref="IAiService"/> com o SDK oficial da Anthropic para C# (NuGet <c>Anthropic</c>) — SPEC-20261027-1312.
/// Antes de chamar: IA ligada, recurso ligado, modelo com preço, limite do mês (0 = nenhum uso pago) e chave legível.
/// Cada chamada (e cada recusa) vira uma linha em <c>ai_usages</c> com tokens, custo e a categoria do erro — nunca o
/// conteúdo. Erros do SDK são tratados do mais específico ao mais geral, com mensagens em pt-BR; a resposta com
/// <c>stop_reason = "refusal"</c> é conferida antes de ler o conteúdo. A URL base vem de <see cref="AiRuntimeOptions"/>
/// (servidor falso nos testes; nunca chave real nem API real em teste).
/// </summary>
public sealed class AnthropicAiService : IAiService
{
    public const string HttpClientName = "anthropic";

    private readonly IAppDbContext _db;
    private readonly IAiSecretProtector _protector;
    private readonly AiRuntimeOptions _options;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<AnthropicAiService> _logger;

    public AnthropicAiService(
        IAppDbContext db,
        IAiSecretProtector protector,
        AiRuntimeOptions options,
        IHttpClientFactory http,
        ILogger<AnthropicAiService> logger)
    {
        _db = db;
        _protector = protector;
        _options = options;
        _http = http;
        _logger = logger;
    }

    public async Task<AiResult> SendAsync(AiRequest request, CancellationToken ct = default)
    {
        var feature = AiFeatureCatalog.Find(request.FeatureKey, _options.Development);
        // Recurso fora do catálogo ou que roda no aparelho não chama a Anthropic: erro de quem chamou, sem registro
        if (feature is null || !feature.IsPaid)
            throw new AiException(AiErrors.FeatureUnknown, AiErrors.FeatureUnknownMessage);

        var settings = await _db.AiSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == AiSettings.SingletonId, ct);
        var model = string.IsNullOrWhiteSpace(request.Model)
            ? settings?.DefaultModel ?? AiModelCatalog.DefaultModel
            : request.Model.Trim();

        if (settings is null || !settings.Enabled)
            throw await RefuseAsync(request, model, AiErrors.AiDisabled, AiErrors.AiDisabledMessage);
        var featureEnabled = await _db.AiFeatures.AsNoTracking()
            .Where(f => f.Key == feature.Key).Select(f => (bool?)f.Enabled).FirstOrDefaultAsync(ct);
        if (featureEnabled == false)
            throw await RefuseAsync(request, model, AiErrors.FeatureDisabled, AiErrors.FeatureDisabledMessage);
        if (string.IsNullOrEmpty(settings.ApiKeyEncrypted))
            throw await RefuseAsync(request, model, AiErrors.NoKey, AiErrors.NoKeyMessage);
        var price = await _db.AiModelPrices.AsNoTracking().FirstOrDefaultAsync(p => p.Model == model, ct);
        if (price is null)
            throw await RefuseAsync(request, model, AiErrors.ModelUnknown, AiErrors.ModelUnknownMessage(model));
        if (settings.MonthlyLimitUsd <= 0)
            throw await RefuseAsync(request, model, AiErrors.MonthlyLimit, AiErrors.ZeroLimitMessage);
        var monthStart = AiCost.MonthStart(DateTimeOffset.UtcNow);
        var spent = await _db.AiUsages.Where(u => u.CreatedAt >= monthStart).SumAsync(u => (decimal?)u.CostUsd, ct) ?? 0m;
        if (spent >= settings.MonthlyLimitUsd)
            throw await RefuseAsync(request, model, AiErrors.MonthlyLimit, AiErrors.MonthlyLimitMessage(spent, settings.MonthlyLimitUsd));
        var apiKey = _protector.Unprotect(settings.ApiKeyEncrypted);
        if (apiKey is null)
            throw await RefuseAsync(request, model, AiErrors.KeyUnreadable, AiErrors.KeyUnreadableMessage);

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = Math.Clamp(request.MaxTokens, 1, 64_000),
            Messages = [new() { Role = Role.User, Content = BuildContent(request) }],
        };
        if (!string.IsNullOrWhiteSpace(request.System))
            parameters = parameters with { System = request.System };

        var watch = Stopwatch.StartNew();
        Message response;
        try
        {
            using var client = CreateClient(apiKey);
            response = await client.Messages.Create(parameters, ct);
        }
        catch (Exception ex) when (Classify(ex, model, ct) is { } failure)
        {
            watch.Stop();
            _logger.LogWarning("IA: chamada de {Feature} ({Model}) falhou: {Code} ({Type})", feature.Key, model, failure.Code, ex.GetType().Name);
            await RecordAsync(request, model, nameof(AiUsageOutcome.Error), default, 0m, failure.Code, failure.Message, (int)watch.ElapsedMilliseconds);
            throw new AiException(failure.Code, failure.Message);
        }
        watch.Stop();

        var usage = new AiTokenUsage(
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.CacheCreationInputTokens ?? 0,
            response.Usage.CacheReadInputTokens ?? 0);
        var cost = AiCost.Compute(price, usage);

        // Recusa do modelo: os tokens foram cobrados; o conteúdo não é lido
        if (response.StopReason == "refusal")
        {
            await RecordAsync(request, model, nameof(AiUsageOutcome.Error), usage, cost, AiErrors.Refusal, AiErrors.RefusalMessage, (int)watch.ElapsedMilliseconds);
            throw new AiException(AiErrors.Refusal, AiErrors.RefusalMessage);
        }

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        var usageId = await RecordAsync(request, model, nameof(AiUsageOutcome.Success), usage, cost, null, null, (int)watch.ElapsedMilliseconds);
        return new AiResult(
            usageId,
            text,
            model,
            response.StopReason?.ToString(),
            usage.InputTokens,
            usage.OutputTokens,
            usage.CacheCreationInputTokens,
            usage.CacheReadInputTokens,
            cost);
    }

    public async Task<AiKeyTestResult> TestKeyAsync(string model, CancellationToken ct = default)
    {
        var settings = await _db.AiSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == AiSettings.SingletonId, ct);
        if (settings is null || string.IsNullOrEmpty(settings.ApiKeyEncrypted))
            return new AiKeyTestResult(false, AiErrors.NoKey, AiErrors.NoKeyMessage, model);
        var apiKey = _protector.Unprotect(settings.ApiKeyEncrypted);
        if (apiKey is null)
            return new AiKeyTestResult(false, AiErrors.KeyUnreadable, AiErrors.KeyUnreadableMessage, model);

        try
        {
            using var client = CreateClient(apiKey);
            // API de modelos: confere a chave e o acesso ao modelo sem gastar tokens
            var info = await client.Models.Retrieve(model, cancellationToken: ct);
            var name = string.IsNullOrWhiteSpace(info.DisplayName) ? model : info.DisplayName;
            return new AiKeyTestResult(true, "ok", $"Chave válida: {name} disponível.", model);
        }
        catch (Exception ex) when (Classify(ex, model, ct) is { } failure)
        {
            _logger.LogWarning("IA: teste da chave ({Model}) falhou: {Code} ({Type})", model, failure.Code, ex.GetType().Name);
            return new AiKeyTestResult(false, failure.Code, failure.Message, model);
        }
    }

    private AnthropicClient CreateClient(string apiKey)
    {
        var http = _http.CreateClient(HttpClientName);
        return _options.BaseUrl is { } baseUrl
            ? new AnthropicClient
            {
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                HttpClient = http,
                MaxRetries = _options.MaxRetries,
                Timeout = _options.Timeout,
            }
            : new AnthropicClient
            {
                ApiKey = apiKey,
                HttpClient = http,
                MaxRetries = _options.MaxRetries,
                Timeout = _options.Timeout,
            };
    }

    private static MessageParamContent BuildContent(AiRequest request)
    {
        if (request.Images.Count == 0)
            return request.Prompt;
        var blocks = new List<ContentBlockParam>();
        foreach (var image in request.Images)
        {
            blocks.Add(new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    Data = Convert.ToBase64String(image.Data),
                    MediaType = image.MediaType switch
                    {
                        "image/png" => MediaType.ImagePng,
                        "image/gif" => MediaType.ImageGif,
                        "image/webp" => MediaType.ImageWebP,
                        _ => MediaType.ImageJpeg,
                    },
                },
            });
        }
        blocks.Add(new TextBlockParam { Text = request.Prompt });
        return blocks;
    }

    /// <summary>Categoria e mensagem em pt-BR do erro do SDK — do mais específico ao mais geral. Null = não é nosso.</summary>
    private static (string Code, string Message)? Classify(Exception ex, string model, CancellationToken ct) => ex switch
    {
        AnthropicUnauthorizedException => (AiErrors.Unauthorized, AiErrors.UnauthorizedMessage),
        AnthropicForbiddenException => (AiErrors.Forbidden, AiErrors.ForbiddenMessage),
        AnthropicNotFoundException => (AiErrors.NotFound, AiErrors.NotFoundMessage(model)),
        AnthropicRateLimitException => (AiErrors.RateLimited, AiErrors.RateLimitedMessage),
        Anthropic5xxException e => (AiErrors.ServerError, AiErrors.ServerErrorMessage((int)e.StatusCode)),
        AnthropicBadRequestException e => (AiErrors.BadRequest, AiErrors.BadRequestMessage((int)e.StatusCode)),
        AnthropicIOException => (AiErrors.Network, AiErrors.NetworkMessage),
        AnthropicApiException e => (AiErrors.ApiError, AiErrors.BadRequestMessage((int)e.StatusCode)),
        // Tempo esgotado do cliente (não foi quem chamou que cancelou)
        OperationCanceledException when !ct.IsCancellationRequested => (AiErrors.Timeout, AiErrors.TimeoutMessage),
        HttpRequestException => (AiErrors.Network, AiErrors.NetworkMessage),
        AnthropicException => (AiErrors.ApiError, "Resposta inesperada da Anthropic. Tente de novo em instantes."),
        _ => null,
    };

    /// <summary>Recusa ANTES de chamar a Anthropic: registra (sem tokens, custo 0) e devolve a exceção para lançar.</summary>
    private async Task<AiException> RefuseAsync(AiRequest request, string model, string code, string message)
    {
        await RecordAsync(request, model, nameof(AiUsageOutcome.Refused), default, 0m, code, message, null);
        return new AiException(code, message);
    }

    private async Task<Guid> RecordAsync(
        AiRequest request, string model, string outcome, AiTokenUsage usage, decimal cost,
        string? errorCode, string? errorMessage, int? durationMs)
    {
        var row = new AiUsage
        {
            FeatureKey = request.FeatureKey,
            Model = model.Length <= 100 ? model : model[..100],
            OnDevice = false,
            UserId = request.Caller.UserId,
            ActorName = request.Caller.Name is { Length: > 200 } n ? n[..200] : request.Caller.Name,
            HomeGameId = request.Caller.HomeGameId,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CacheCreationInputTokens = usage.CacheCreationInputTokens,
            CacheReadInputTokens = usage.CacheReadInputTokens,
            CostUsd = cost,
            Outcome = outcome,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage is { Length: > 300 } m ? m[..300] : errorMessage,
            DurationMs = durationMs,
        };
        _db.AiUsages.Add(row);
        // O registro sai mesmo que quem chamou desista no meio (gasto já feito precisa contar no limite)
        await _db.SaveChangesAsync(CancellationToken.None);
        return row.Id;
    }
}
