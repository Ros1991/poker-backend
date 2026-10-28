using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Interfaces;

/// <summary>
/// Serviço ÚNICO de IA (SPEC-20261027-1312): todo recurso que chama o Claude passa por aqui. Antes de cada chamada
/// confere liga/desliga geral e do recurso, chave configurada e limite do mês; chama pelo SDK oficial da Anthropic
/// (Messages API, texto e imagem); registra a chamada em <c>ai_usages</c> (tokens, custo pela tabela de preços,
/// sucesso/erro com a categoria) SEM guardar o conteúdo; e devolve erro em pt-BR como <see cref="AiException"/>.
/// </summary>
public interface IAiService
{
    /// <summary>Uma mensagem ao Claude. Lança <see cref="AiException"/> quando recusa ou quando a chamada falha.</summary>
    Task<AiResult> SendAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>"Testar chave": valida a chave guardada e o modelo pela API de modelos (sem custo de tokens).</summary>
    Task<AiKeyTestResult> TestKeyAsync(string model, CancellationToken ct = default);
}

/// <summary>Imagem enviada junto do texto (JPEG, PNG, GIF ou WebP).</summary>
public sealed record AiImage(byte[] Data, string MediaType);

/// <summary>Quem pediu (para o registro de uso): conta, nome mostrado e home game.</summary>
public sealed record AiCaller(Guid? UserId, string? Name, Guid? HomeGameId);

public sealed class AiRequest
{
    /// <summary>Chave do recurso no <c>AiFeatureCatalog</c>.</summary>
    public required string FeatureKey { get; init; }
    public required string Prompt { get; init; }
    public IReadOnlyList<AiImage> Images { get; init; } = [];
    public string? System { get; init; }
    /// <summary>Null = modelo padrão da Administração.</summary>
    public string? Model { get; init; }
    public int MaxTokens { get; init; } = 1024;
    public AiCaller Caller { get; init; } = new(null, null, null);
}

public sealed record AiResult(
    Guid UsageId,
    string Text,
    string Model,
    string? StopReason,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    decimal CostUsd);

public sealed record AiKeyTestResult(bool Ok, string Code, string Message, string Model);

/// <summary>
/// Recusa ou falha do serviço de IA, com <see cref="Code"/> (ver <see cref="AiErrors"/>) e mensagem em pt-BR. Herda de
/// <see cref="DomainException"/> (vira 400 com a mensagem no ErrorHandlingMiddleware).
/// </summary>
public sealed class AiException : DomainException
{
    public string Code { get; }

    public AiException(string code, string message) : base(message)
    {
        Code = code;
    }
}

/// <summary>Categorias de erro/recusa do serviço de IA e as mensagens em pt-BR.</summary>
public static class AiErrors
{
    public const string AiDisabled = "ai_disabled";
    public const string FeatureDisabled = "feature_disabled";
    public const string FeatureUnknown = "feature_unknown";
    public const string NoKey = "no_key";
    public const string KeyUnreadable = "key_unreadable";
    public const string MonthlyLimit = "monthly_limit";
    public const string ModelUnknown = "model_unknown";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "model_not_found";
    public const string RateLimited = "rate_limited";
    public const string ServerError = "server_error";
    public const string Network = "network";
    public const string Timeout = "timeout";
    public const string BadRequest = "bad_request";
    public const string ApiError = "api_error";
    public const string Refusal = "refusal";

    public const string AiDisabledMessage = "A inteligência artificial está desligada na Administração.";
    public const string FeatureDisabledMessage = "Este recurso de IA está desligado na Administração.";
    public const string FeatureUnknownMessage = "Recurso de IA desconhecido.";
    public const string NoKeyMessage = "Nenhuma chave da Anthropic cadastrada na Administração.";
    public const string KeyUnreadableMessage =
        "A chave guardada não pôde ser lida (a chave mestra AI_SECRETS_KEY mudou ou não está configurada). Cadastre a chave de novo.";
    public const string ZeroLimitMessage =
        "O limite mensal de IA é US$ 0: nenhum uso pago está liberado. Defina um limite na Administração.";
    public const string UnauthorizedMessage = "Chave inválida: a Anthropic recusou a chave. Confira ou troque a chave.";
    public const string ForbiddenMessage = "Sem permissão: a chave não tem acesso a este recurso ou modelo.";
    public const string RateLimitedMessage = "Limite de requisições da Anthropic atingido. Tente de novo em instantes.";
    public const string NetworkMessage = "Sem conexão com a Anthropic. Confira a internet do servidor e tente de novo.";
    public const string TimeoutMessage = "A Anthropic demorou demais para responder. Tente de novo em instantes.";
    public const string RefusalMessage = "O modelo se recusou a responder este pedido.";

    public static string MonthlyLimitMessage(decimal spent, decimal limit) =>
        $"Limite mensal de IA atingido (gasto de US$ {Usd(spent)} de US$ {Usd(limit)}). Aumente o limite na Administração ou espere o próximo mês.";

    public static string ModelUnknownMessage(string model) => $"O modelo {model} não tem preço cadastrado na Administração.";

    public static string NotFoundMessage(string model) => $"Modelo indisponível para esta chave ({model}).";

    public static string ServerErrorMessage(int status) =>
        $"A Anthropic está com instabilidade (erro {status}). Tente de novo em instantes.";

    public static string BadRequestMessage(int status) => $"A Anthropic recusou o pedido (erro {status}).";

    /// <summary>Valor em dólar com vírgula decimal (pt-BR): 12.5 → "12,50".</summary>
    public static string Usd(decimal value) =>
        value.ToString(value != 0 && Math.Abs(value) < 0.01m ? "0.0000" : "0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}

/// <summary>Chave mestra AES-256-GCM da chave da API (variável AI_SECRETS_KEY).</summary>
public interface IAiSecretProtector
{
    /// <summary>A chave mestra existe e é válida (32 bytes em base64).</summary>
    bool IsConfigured { get; }

    /// <summary>Cifra; lança InvalidOperationException sem chave mestra.</summary>
    string Protect(string plaintext);

    /// <summary>Decifra; null quando não dá (sem chave mestra, chave mestra trocada, texto adulterado).</summary>
    string? Unprotect(string ciphertext);
}
