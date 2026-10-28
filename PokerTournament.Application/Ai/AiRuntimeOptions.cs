namespace PokerTournament.Application.Ai;

/// <summary>
/// Configuração de execução do serviço de IA (singleton, SPEC-20261027-1312).
/// <list type="bullet">
/// <item><see cref="BaseUrl"/>: URL base da API da Anthropic. Vazia = a de produção do SDK. Vem de <c>AI_BASE_URL</c>
/// (ou <c>Ai:BaseUrl</c>); em Development os testes a apontam para o servidor falso pelo endpoint
/// <c>POST api/v1/dev/ai/base-url</c> (que só existe em Development).</item>
/// <item><see cref="Development"/>: liga o recurso pago de teste do catálogo.</item>
/// </list>
/// </summary>
public sealed class AiRuntimeOptions
{
    private string? _baseUrl;

    public bool Development { get; init; }

    /// <summary>Tentativas extras do SDK em 408/409/429/5xx/rede (padrão 2, como o próprio SDK).</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Tempo máximo de uma chamada (sem contar as tentativas extras).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    public string? BaseUrl
    {
        get => Volatile.Read(ref _baseUrl);
        set => Volatile.Write(ref _baseUrl, string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/'));
    }
}
