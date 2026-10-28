using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Ai;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Tela "Inteligência artificial" da Administração (SPEC-20261027-1312), só Admin: chave da Anthropic cifrada (nunca
/// devolvida), testar chave pela API de modelos, liga/desliga geral e por recurso, modelo padrão, limite mensal, uso do
/// mês por recurso e por modelo, últimas chamadas e preços editáveis. Toda alteração grava AuditLog (sem a chave).
/// </summary>
public class AiAdminService
{
    public const string NoSecretsKeyMessage =
        "A chave mestra AI_SECRETS_KEY não está configurada no servidor: sem ela a chave da API não pode ser guardada com segurança.";
    public const string KeyRequiredMessage = "Informe a chave da API da Anthropic.";
    public const string KeyFormatMessage = "Chave inválida: a chave da Anthropic começa com \"sk-ant-\" e não tem espaços.";
    public const string LimitRangeMessage = "O limite mensal vai de US$ 0 a US$ 100.000, com até 2 casas decimais.";
    public const string PriceRangeMessage = "Os preços vão de US$ 0 a US$ 10.000 por milhão de tokens, com até 4 casas decimais.";
    public const string FeatureNotFoundMessage = "Recurso de IA não encontrado.";
    public const string ModelNotFoundMessage = "Modelo não encontrado na tabela de preços.";

    private const string AuditEntity = "AiSettings";
    private const int RecentCallsCount = 30;
    private const string KeyPrefix = "sk-ant-";

    private readonly IAppDbContext _db;
    private readonly IAiService _ai;
    private readonly IAiSecretProtector _protector;
    private readonly AiRuntimeOptions _options;

    public AiAdminService(IAppDbContext db, IAiService ai, IAiSecretProtector protector, AiRuntimeOptions options)
    {
        _db = db;
        _ai = ai;
        _protector = protector;
        _options = options;
    }

    /// <summary>
    /// Idempotente (subida da API): cria a linha única de configurações (IA ligada, Opus 5.5, limite US$ 0), as linhas
    /// dos recursos do catálogo (ligados) e os preços dos modelos que ainda não existem. Recurso que já existe só tem
    /// nome/descrição/tipo atualizados do catálogo — o liga/desliga do Admin fica como está.
    /// </summary>
    public async Task EnsureCatalogAsync(CancellationToken ct = default)
    {
        // INSERT … ON CONFLICT: várias instâncias subindo juntas no mesmo banco (testes) não disputam a mesma linha
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO ai_settings (id, enabled, default_model, monthly_limit_usd, created_at, updated_at)
            VALUES ({AiSettings.SingletonId}, TRUE, {AiModelCatalog.DefaultModel}, 0, now(), now())
            ON CONFLICT (id) DO NOTHING
            """, ct);

        foreach (var def in AiFeatureCatalog.For(_options.Development))
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO ai_features (id, key, name, description, is_paid, runs_on_device, enabled, sort_order, created_at, updated_at)
                VALUES ({Guid.NewGuid()}, {def.Key}, {def.Name}, {def.Description}, {def.IsPaid}, {def.RunsOnDevice}, TRUE, {def.SortOrder}, now(), now())
                ON CONFLICT (key) DO UPDATE SET
                    name = EXCLUDED.name, description = EXCLUDED.description, is_paid = EXCLUDED.is_paid,
                    runs_on_device = EXCLUDED.runs_on_device, sort_order = EXCLUDED.sort_order, updated_at = now()
                WHERE ai_features.name IS DISTINCT FROM EXCLUDED.name
                   OR ai_features.description IS DISTINCT FROM EXCLUDED.description
                   OR ai_features.is_paid IS DISTINCT FROM EXCLUDED.is_paid
                   OR ai_features.runs_on_device IS DISTINCT FROM EXCLUDED.runs_on_device
                   OR ai_features.sort_order IS DISTINCT FROM EXCLUDED.sort_order
                """, ct);
        }

        foreach (var def in AiModelCatalog.Models)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO ai_model_prices (id, model, display_name, input_per_mtok, output_per_mtok, cache_write_per_mtok,
                    cache_read_per_mtok, long_context_threshold_tokens, long_input_per_mtok, long_output_per_mtok,
                    long_cache_write_per_mtok, long_cache_read_per_mtok, sort_order, created_at, updated_at)
                VALUES ({Guid.NewGuid()}, {def.Model}, {def.DisplayName}, {def.InputPerMTok}, {def.OutputPerMTok},
                    {def.CacheWritePerMTok}, {def.CacheReadPerMTok}, {def.LongContextThresholdTokens}, {def.LongInputPerMTok},
                    {def.LongOutputPerMTok}, {def.LongCacheWritePerMTok}, {def.LongCacheReadPerMTok}, {def.SortOrder}, now(), now())
                ON CONFLICT (model) DO NOTHING
                """, ct);
        }
    }

    public async Task<AiOverviewResponse> GetOverviewAsync(CancellationToken ct = default)
    {
        var settings = await LoadSettingsAsync(tracked: false, ct);
        var catalog = AiFeatureCatalog.For(_options.Development);
        var featureRows = await _db.AiFeatures.AsNoTracking().ToListAsync(ct);
        var prices = await _db.AiModelPrices.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.Model).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var monthStart = AiCost.MonthStart(now);
        var success = nameof(AiUsageOutcome.Success);
        var error = nameof(AiUsageOutcome.Error);
        var refused = nameof(AiUsageOutcome.Refused);

        var month = _db.AiUsages.AsNoTracking().Where(u => u.CreatedAt >= monthStart);
        var spent = await month.SumAsync(u => (decimal?)u.CostUsd, ct) ?? 0m;

        var byFeatureRows = await month
            .GroupBy(u => u.FeatureKey)
            .Select(g => new
            {
                Key = g.Key,
                Calls = g.Count(u => u.Outcome == success || u.Outcome == error),
                Errors = g.Count(u => u.Outcome == error),
                Refused = g.Count(u => u.Outcome == refused),
                Input = g.Sum(u => u.InputTokens),
                Output = g.Sum(u => u.OutputTokens),
                CacheWrite = g.Sum(u => u.CacheCreationInputTokens),
                CacheRead = g.Sum(u => u.CacheReadInputTokens),
                Cost = g.Sum(u => u.CostUsd),
            })
            .ToListAsync(ct);
        var byModelRows = await month
            .Where(u => u.Model != null || u.OnDevice)
            .GroupBy(u => u.Model)
            .Select(g => new
            {
                Key = g.Key,
                Calls = g.Count(u => u.Outcome == success || u.Outcome == error),
                Errors = g.Count(u => u.Outcome == error),
                Refused = g.Count(u => u.Outcome == refused),
                Input = g.Sum(u => u.InputTokens),
                Output = g.Sum(u => u.OutputTokens),
                CacheWrite = g.Sum(u => u.CacheCreationInputTokens),
                CacheRead = g.Sum(u => u.CacheReadInputTokens),
                Cost = g.Sum(u => u.CostUsd),
            })
            .ToListAsync(ct);

        var recent = await _db.AiUsages.AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .Take(RecentCallsCount)
            .ToListAsync(ct);
        var homeGameIds = recent.Where(u => u.HomeGameId != null).Select(u => u.HomeGameId!.Value).Distinct().ToList();
        var homeGameNames = homeGameIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.HomeGames.IgnoreQueryFilters().AsNoTracking()
                .Where(h => homeGameIds.Contains(h.Id))
                .ToDictionaryAsync(h => h.Id, h => h.Name, ct);

        string FeatureName(string key) =>
            catalog.FirstOrDefault(f => f.Key == key)?.Name
            ?? featureRows.FirstOrDefault(f => f.Key == key)?.Name
            ?? key;
        string ModelName(string? model) =>
            model is null ? "No aparelho" : prices.FirstOrDefault(p => p.Model == model)?.DisplayName ?? model;

        string? updatedByName = null;
        if (settings.ApiKeyUpdatedBy is { } by)
            updatedByName = await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == by).Select(u => (string?)u.FullName).FirstOrDefaultAsync(ct);

        var keyConfigured = !string.IsNullOrEmpty(settings.ApiKeyEncrypted);
        var limit = settings.MonthlyLimitUsd;
        decimal? percent = limit > 0 ? Math.Round(spent / limit * 100m, 1, MidpointRounding.AwayFromZero) : null;

        return new AiOverviewResponse
        {
            Enabled = settings.Enabled,
            DefaultModel = settings.DefaultModel,
            MonthlyLimitUsd = limit,
            SecretsKeyConfigured = _protector.IsConfigured,
            Key = new AiKeyInfoResponse
            {
                Configured = keyConfigured,
                Hint = keyConfigured ? KeyHint(settings.ApiKeyLast4) : null,
                UpdatedAt = keyConfigured ? settings.ApiKeyUpdatedAt : null,
                UpdatedByName = keyConfigured ? updatedByName : null,
                Unreadable = keyConfigured && _protector.Unprotect(settings.ApiKeyEncrypted!) is null,
            },
            LastTest = keyConfigured && settings.LastTestAt is { } testAt
                ? new AiKeyTestInfoResponse
                {
                    At = testAt,
                    Ok = settings.LastTestOk == true,
                    Message = settings.LastTestMessage ?? string.Empty,
                    Model = settings.LastTestModel,
                }
                : null,
            Month = new AiMonthResponse
            {
                Start = monthStart,
                SpentUsd = spent,
                LimitUsd = limit,
                Percent = percent,
                Warning = limit > 0 && spent >= limit * 0.8m,
                Blocked = limit <= 0 || spent >= limit,
            },
            Features = catalog
                .OrderBy(f => f.SortOrder)
                .Select(f =>
                {
                    var row = featureRows.FirstOrDefault(r => r.Key == f.Key);
                    var usage = byFeatureRows.FirstOrDefault(u => u.Key == f.Key);
                    return new AiFeatureResponse
                    {
                        Key = f.Key,
                        Name = f.Name,
                        Description = f.Description,
                        IsPaid = f.IsPaid,
                        RunsOnDevice = f.RunsOnDevice,
                        Enabled = row?.Enabled ?? true,
                        MonthUses = usage?.Calls ?? 0,
                    };
                })
                .ToList(),
            Prices = prices.Select(p => new AiModelPriceResponse
            {
                Model = p.Model,
                DisplayName = p.DisplayName,
                InputPerMTok = p.InputPerMTok,
                OutputPerMTok = p.OutputPerMTok,
                CacheWritePerMTok = p.CacheWritePerMTok,
                CacheReadPerMTok = p.CacheReadPerMTok,
                LongContextThresholdTokens = p.LongContextThresholdTokens,
                LongInputPerMTok = p.LongInputPerMTok,
                LongOutputPerMTok = p.LongOutputPerMTok,
                LongCacheWritePerMTok = p.LongCacheWritePerMTok,
                LongCacheReadPerMTok = p.LongCacheReadPerMTok,
                IsDefault = p.Model == settings.DefaultModel,
            }).ToList(),
            UsageByFeature = byFeatureRows
                .Select(u => new AiUsageSummaryResponse
                {
                    Key = u.Key,
                    Name = FeatureName(u.Key),
                    Calls = u.Calls,
                    Errors = u.Errors,
                    Refused = u.Refused,
                    InputTokens = u.Input,
                    OutputTokens = u.Output,
                    CacheCreationInputTokens = u.CacheWrite,
                    CacheReadInputTokens = u.CacheRead,
                    CostUsd = u.Cost,
                })
                .OrderByDescending(u => u.CostUsd).ThenByDescending(u => u.Calls).ThenBy(u => u.Name)
                .ToList(),
            UsageByModel = byModelRows
                .Select(u => new AiUsageSummaryResponse
                {
                    Key = u.Key ?? "no-aparelho",
                    Name = ModelName(u.Key),
                    Calls = u.Calls,
                    Errors = u.Errors,
                    Refused = u.Refused,
                    InputTokens = u.Input,
                    OutputTokens = u.Output,
                    CacheCreationInputTokens = u.CacheWrite,
                    CacheReadInputTokens = u.CacheRead,
                    CostUsd = u.Cost,
                })
                .OrderByDescending(u => u.CostUsd).ThenByDescending(u => u.Calls).ThenBy(u => u.Name)
                .ToList(),
            RecentCalls = recent.Select(u => new AiUsageItemResponse
            {
                Id = u.Id,
                At = u.CreatedAt,
                ActorName = u.ActorName,
                HomeGameName = u.HomeGameId is { } hg && homeGameNames.TryGetValue(hg, out var name) ? name : null,
                FeatureKey = u.FeatureKey,
                FeatureName = FeatureName(u.FeatureKey),
                Model = u.Model,
                ModelName = u.Model is null ? null : ModelName(u.Model),
                OnDevice = u.OnDevice,
                InputTokens = u.InputTokens,
                OutputTokens = u.OutputTokens,
                CacheCreationInputTokens = u.CacheCreationInputTokens,
                CacheReadInputTokens = u.CacheReadInputTokens,
                CostUsd = u.CostUsd,
                Outcome = u.Outcome,
                ErrorCode = u.ErrorCode,
                ErrorMessage = u.ErrorMessage,
                DurationMs = u.DurationMs,
            }).ToList(),
        };
    }

    public async Task<AiOverviewResponse> UpdateSettingsAsync(UpdateAiSettingsRequest request, AdminActor actor, CancellationToken ct = default)
    {
        var model = (request.DefaultModel ?? string.Empty).Trim();
        if (!await _db.AiModelPrices.AnyAsync(p => p.Model == model, ct))
            throw new DomainException(ModelNotFoundMessage);
        var limit = request.MonthlyLimitUsd;
        if (limit < 0 || limit > 100_000m || decimal.Round(limit, 2) != limit)
            throw new DomainException(LimitRangeMessage);

        var settings = await LoadSettingsAsync(tracked: true, ct);
        var old = new { settings.Enabled, settings.DefaultModel, settings.MonthlyLimitUsd };
        settings.Enabled = request.Enabled;
        settings.DefaultModel = model;
        settings.MonthlyLimitUsd = limit;
        await AddAuditAsync("AiSettingsChange", old, new { settings.Enabled, settings.DefaultModel, settings.MonthlyLimitUsd }, actor, ct);
        await _db.SaveChangesAsync(ct);
        return await GetOverviewAsync(ct);
    }

    public async Task<AiOverviewResponse> SetKeyAsync(SetAiKeyRequest request, AdminActor actor, CancellationToken ct = default)
    {
        if (!_protector.IsConfigured)
            throw new InvalidOperationException(NoSecretsKeyMessage);
        var key = (request.ApiKey ?? string.Empty).Trim();
        if (key.Length == 0)
            throw new DomainException(KeyRequiredMessage);
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal) || key.Length < 20 || key.Length > 300
            || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new DomainException(KeyFormatMessage);

        var settings = await LoadSettingsAsync(tracked: true, ct);
        var hadKey = !string.IsNullOrEmpty(settings.ApiKeyEncrypted);
        var oldHint = hadKey ? KeyHint(settings.ApiKeyLast4) : null;
        settings.ApiKeyEncrypted = _protector.Protect(key);
        settings.ApiKeyLast4 = key[^4..];
        settings.ApiKeyUpdatedAt = DateTimeOffset.UtcNow;
        settings.ApiKeyUpdatedBy = actor.UserId == Guid.Empty ? null : actor.UserId;
        // Chave nova: o teste anterior não vale mais
        settings.LastTestAt = null;
        settings.LastTestOk = null;
        settings.LastTestMessage = null;
        settings.LastTestModel = null;
        await AddAuditAsync(hadKey ? "AiKeyReplace" : "AiKeySet", new { hint = oldHint }, new { hint = KeyHint(settings.ApiKeyLast4) }, actor, ct);
        await _db.SaveChangesAsync(ct);
        return await GetOverviewAsync(ct);
    }

    public async Task<AiOverviewResponse> RemoveKeyAsync(AdminActor actor, CancellationToken ct = default)
    {
        var settings = await LoadSettingsAsync(tracked: true, ct);
        if (!string.IsNullOrEmpty(settings.ApiKeyEncrypted))
        {
            var oldHint = KeyHint(settings.ApiKeyLast4);
            settings.ApiKeyEncrypted = null;
            settings.ApiKeyLast4 = null;
            settings.ApiKeyUpdatedAt = DateTimeOffset.UtcNow;
            settings.ApiKeyUpdatedBy = actor.UserId == Guid.Empty ? null : actor.UserId;
            settings.LastTestAt = null;
            settings.LastTestOk = null;
            settings.LastTestMessage = null;
            settings.LastTestModel = null;
            await AddAuditAsync("AiKeyRemove", new { hint = oldHint }, null, actor, ct);
            await _db.SaveChangesAsync(ct);
        }
        return await GetOverviewAsync(ct);
    }

    public async Task<AiKeyTestResponse> TestKeyAsync(TestAiKeyRequest request, AdminActor actor, CancellationToken ct = default)
    {
        var settings = await LoadSettingsAsync(tracked: true, ct);
        var model = string.IsNullOrWhiteSpace(request.Model) ? settings.DefaultModel : request.Model.Trim();
        if (!await _db.AiModelPrices.AnyAsync(p => p.Model == model, ct))
            throw new DomainException(ModelNotFoundMessage);

        var result = await _ai.TestKeyAsync(model, ct);
        if (!string.IsNullOrEmpty(settings.ApiKeyEncrypted))
        {
            settings.LastTestAt = DateTimeOffset.UtcNow;
            settings.LastTestOk = result.Ok;
            settings.LastTestMessage = Cut(result.Message, 300);
            settings.LastTestModel = model;
        }
        await AddAuditAsync("AiKeyTest", null, new { model, ok = result.Ok, code = result.Code }, actor, ct);
        await _db.SaveChangesAsync(ct);
        return new AiKeyTestResponse { Ok = result.Ok, Code = result.Code, Message = result.Message, Model = result.Model };
    }

    public async Task<AiOverviewResponse> SetFeatureAsync(string key, bool enabled, AdminActor actor, CancellationToken ct = default)
    {
        var def = AiFeatureCatalog.Find(key, _options.Development)
            ?? throw new KeyNotFoundException(FeatureNotFoundMessage);
        var row = await _db.AiFeatures.FirstOrDefaultAsync(f => f.Key == def.Key, ct);
        if (row is null)
        {
            row = new AiFeature
            {
                Key = def.Key,
                Name = def.Name,
                Description = def.Description,
                IsPaid = def.IsPaid,
                RunsOnDevice = def.RunsOnDevice,
                SortOrder = def.SortOrder,
                Enabled = true,
            };
            _db.AiFeatures.Add(row);
        }
        var old = row.Enabled;
        row.Enabled = enabled;
        await AddAuditAsync("AiFeatureChange", new { feature = def.Key, enabled = old }, new { feature = def.Key, enabled }, actor, ct);
        await _db.SaveChangesAsync(ct);
        return await GetOverviewAsync(ct);
    }

    public async Task<AiOverviewResponse> UpdatePriceAsync(string model, UpdateAiPriceRequest request, AdminActor actor, CancellationToken ct = default)
    {
        var price = await _db.AiModelPrices.FirstOrDefaultAsync(p => p.Model == model, ct)
            ?? throw new KeyNotFoundException(ModelNotFoundMessage);
        ValidatePrice(request.InputPerMTok);
        ValidatePrice(request.OutputPerMTok);
        ValidatePrice(request.CacheWritePerMTok);
        ValidatePrice(request.CacheReadPerMTok);
        var hasLong = price.LongContextThresholdTokens is not null;
        if (hasLong)
        {
            ValidatePrice(request.LongInputPerMTok ?? 0m);
            ValidatePrice(request.LongOutputPerMTok ?? 0m);
            ValidatePrice(request.LongCacheWritePerMTok ?? 0m);
            ValidatePrice(request.LongCacheReadPerMTok ?? 0m);
        }

        var old = Snapshot(price);
        price.InputPerMTok = request.InputPerMTok;
        price.OutputPerMTok = request.OutputPerMTok;
        price.CacheWritePerMTok = request.CacheWritePerMTok;
        price.CacheReadPerMTok = request.CacheReadPerMTok;
        if (hasLong)
        {
            price.LongInputPerMTok = request.LongInputPerMTok ?? price.LongInputPerMTok;
            price.LongOutputPerMTok = request.LongOutputPerMTok ?? price.LongOutputPerMTok;
            price.LongCacheWritePerMTok = request.LongCacheWritePerMTok ?? price.LongCacheWritePerMTok;
            price.LongCacheReadPerMTok = request.LongCacheReadPerMTok ?? price.LongCacheReadPerMTok;
        }
        await AddAuditAsync("AiPriceChange", old, Snapshot(price), actor, ct);
        await _db.SaveChangesAsync(ct);
        return await GetOverviewAsync(ct);
    }

    private static object Snapshot(AiModelPrice p) => new
    {
        p.Model,
        p.InputPerMTok,
        p.OutputPerMTok,
        p.CacheWritePerMTok,
        p.CacheReadPerMTok,
        p.LongInputPerMTok,
        p.LongOutputPerMTok,
        p.LongCacheWritePerMTok,
        p.LongCacheReadPerMTok,
    };

    private static void ValidatePrice(decimal value)
    {
        if (value < 0 || value > 10_000m || decimal.Round(value, 4) != value)
            throw new DomainException(PriceRangeMessage);
    }

    /// <summary>A linha única (criada na subida da API; se faltar, cria aqui com os padrões).</summary>
    private async Task<AiSettings> LoadSettingsAsync(bool tracked, CancellationToken ct)
    {
        var query = tracked ? _db.AiSettings : _db.AiSettings.AsNoTracking();
        var settings = await query.FirstOrDefaultAsync(s => s.Id == AiSettings.SingletonId, ct);
        if (settings is not null)
            return settings;
        await EnsureCatalogAsync(ct);
        return await (tracked ? _db.AiSettings : _db.AiSettings.AsNoTracking())
            .FirstAsync(s => s.Id == AiSettings.SingletonId, ct);
    }

    /// <summary>"sk-ant-…ABCD": o prefixo fixo de toda chave da Anthropic + os 4 últimos caracteres.</summary>
    public static string KeyHint(string? last4) => $"{KeyPrefix}…{last4}";

    private async Task AddAuditAsync(string action, object? oldValues, object? newValues, AdminActor actor, CancellationToken ct)
    {
        var actorName = await _db.Users.AsNoTracking().Where(u => u.Id == actor.UserId)
            .Select(u => (string?)u.FullName).FirstOrDefaultAsync(ct);
        _db.AuditLogs.Add(new AuditLog
        {
            // AuditLog não é BaseEntity: Id e CreatedAt à mão
            Id = Guid.NewGuid(),
            EntityType = AuditEntity,
            EntityId = AiSettings.SingletonId,
            Action = action,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues),
            UserId = actor.UserId == Guid.Empty ? null : actor.UserId,
            UserName = actorName,
            IpAddress = Cut(actor.Ip, 50),
            UserAgent = Cut(actor.UserAgent, 500),
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static string? Cut(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
