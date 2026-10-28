namespace PokerTournament.Application.Ai;

/// <summary>Um recurso de IA declarado no código (SPEC-20261027-1312): chave estável, nome, descrição e se é pago.</summary>
public sealed record AiFeatureDefinition(
    string Key,
    string Name,
    string Description,
    bool IsPaid,
    bool RunsOnDevice,
    int SortOrder);

/// <summary>
/// Catálogo dos recursos de IA. Recurso novo entra AQUI (chave, nome, descrição, pago ou não); na subida da API o
/// <c>AiAdminService.EnsureCatalogAsync</c> cria a linha em <c>ai_features</c> (ligada) e a tela da Administração já
/// mostra o liga/desliga e o uso dele. Todo recurso pago chama o Claude pelo <c>IAiService</c>, que confere liga/desliga
/// geral e do recurso, chave e limite do mês antes de cada chamada.
/// </summary>
public static class AiFeatureCatalog
{
    public const string BackgroundRemovalKey = "tirar-o-fundo";
    public const string DevelopmentTestKey = "teste-desenvolvimento";

    /// <summary>"Tirar o fundo" da foto do jogador: roda no próprio aparelho (MediaPipe), sem chave e sem custo.</summary>
    public static readonly AiFeatureDefinition BackgroundRemoval = new(
        BackgroundRemovalKey,
        "Tirar o fundo da foto",
        "No próprio aparelho, sem custo: recorta a pessoa e põe um fundo liso claro na foto do jogador.",
        IsPaid: false,
        RunsOnDevice: true,
        SortOrder: 10);

    /// <summary>
    /// Chamada paga de teste — SÓ em Development (o catálogo de produção não a tem): é o recurso que os testes do
    /// <c>IAiService</c> usam contra o servidor falso da Anthropic.
    /// </summary>
    public static readonly AiFeatureDefinition DevelopmentTest = new(
        DevelopmentTestKey,
        "Chamada de teste (só em desenvolvimento)",
        "Recurso pago usado só nos testes do serviço de IA; não existe em produção.",
        IsPaid: true,
        RunsOnDevice: false,
        SortOrder: 900);

    /// <summary>Recursos deste ambiente (o de teste só em Development).</summary>
    public static IReadOnlyList<AiFeatureDefinition> For(bool development) =>
        development ? [BackgroundRemoval, DevelopmentTest] : [BackgroundRemoval];

    public static AiFeatureDefinition? Find(string key, bool development) =>
        For(development).FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));
}

/// <summary>Modelo com os preços oficiais de semente (US$ por milhão de tokens).</summary>
public sealed record AiModelDefinition(
    string Model,
    string DisplayName,
    decimal InputPerMTok,
    decimal OutputPerMTok,
    decimal CacheWritePerMTok,
    decimal CacheReadPerMTok,
    int SortOrder,
    int? LongContextThresholdTokens = null,
    decimal? LongInputPerMTok = null,
    decimal? LongOutputPerMTok = null,
    decimal? LongCacheWritePerMTok = null,
    decimal? LongCacheReadPerMTok = null);

/// <summary>
/// Modelos oferecidos na tela de IA e os preços de semente — página oficial de preços da Anthropic conferida em
/// 2026-10-27 (platform.claude.com/docs/en/about-claude/pricing): escrita de cache = 5 minutos; leitura = acerto/renovação.
/// O Admin edita os preços depois; a semente só entra quando o modelo ainda não tem linha.
/// </summary>
public static class AiModelCatalog
{
    public const string DefaultModel = "claude-opus-5-5";

    public static readonly IReadOnlyList<AiModelDefinition> Models =
    [
        new("claude-opus-5-5", "Claude Opus 5.5", 4m, 20m, 5m, 0.20m, SortOrder: 10),
        new("claude-sonnet-5-5", "Claude Sonnet 5.5", 2m, 10m, 2.50m, 0.10m, SortOrder: 20),
        // Haiku 5.5: prompt acima de 100.000 tokens (entrada + escrita + leitura de cache) paga a tabela "longa"
        new("claude-haiku-5-5", "Claude Haiku 5.5", 0.10m, 0.50m, 0.125m, 0.01m, SortOrder: 30,
            LongContextThresholdTokens: 100_000, LongInputPerMTok: 0.50m, LongOutputPerMTok: 2.50m,
            LongCacheWritePerMTok: 0.625m, LongCacheReadPerMTok: 0.05m),
    ];
}
