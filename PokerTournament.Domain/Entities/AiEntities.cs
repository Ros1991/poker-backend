namespace PokerTournament.Domain.Entities;

/// <summary>
/// Configurações de IA do sistema (SPEC-20261027-1312) — LINHA ÚNICA (id fixo <see cref="SingletonId"/>), editada só pelo
/// Admin na tela "Inteligência artificial". A chave da Anthropic fica cifrada (AES-256-GCM com a chave mestra de
/// <c>AI_SECRETS_KEY</c>) e nunca volta ao navegador: só a dica (4 últimos caracteres).
/// </summary>
public class AiSettings : BaseEntity
{
    public static readonly Guid SingletonId = new("6a1e5e77-1312-4c1a-9d5e-000000000001");

    /// <summary>Liga/desliga geral: desligada, nenhum recurso de IA funciona (nem o "Tirar o fundo").</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Modelo usado quando o recurso não pede outro (ex.: "claude-opus-5-5").</summary>
    public string DefaultModel { get; set; } = string.Empty;
    /// <summary>Limite de gasto do mês em US$ (0 = nenhum uso pago).</summary>
    public decimal MonthlyLimitUsd { get; set; }

    /// <summary>Chave da API cifrada (base64 de nonce ‖ texto cifrado ‖ tag). Null = sem chave.</summary>
    public string? ApiKeyEncrypted { get; set; }
    /// <summary>Últimos 4 caracteres da chave, para mostrar "sk-ant-…ABCD".</summary>
    public string? ApiKeyLast4 { get; set; }
    public DateTimeOffset? ApiKeyUpdatedAt { get; set; }
    public Guid? ApiKeyUpdatedBy { get; set; }

    /// <summary>Último "Testar chave" (API de modelos, sem custo).</summary>
    public DateTimeOffset? LastTestAt { get; set; }
    public bool? LastTestOk { get; set; }
    public string? LastTestMessage { get; set; }
    public string? LastTestModel { get; set; }
}

/// <summary>
/// Preço de um modelo do Claude em US$ por milhão de tokens (editável pelo Admin; semeado com a página oficial de
/// preços da Anthropic). O custo de cada chamada sai daqui e dos tokens do <c>Usage</c>. Quando
/// <see cref="LongContextThresholdTokens"/> existe (Claude Haiku 5.5: 100.000), prompt acima dele usa os preços "Long".
/// </summary>
public class AiModelPrice : BaseEntity
{
    public string Model { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal InputPerMTok { get; set; }
    public decimal OutputPerMTok { get; set; }
    /// <summary>Escrita de cache (5 minutos).</summary>
    public decimal CacheWritePerMTok { get; set; }
    /// <summary>Leitura de cache (acerto e renovação).</summary>
    public decimal CacheReadPerMTok { get; set; }
    public int? LongContextThresholdTokens { get; set; }
    public decimal? LongInputPerMTok { get; set; }
    public decimal? LongOutputPerMTok { get; set; }
    public decimal? LongCacheWritePerMTok { get; set; }
    public decimal? LongCacheReadPerMTok { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Recurso de IA com liga/desliga próprio. O catálogo (chave, nome, descrição, pago ou não) é declarado no código
/// (<c>AiFeatureCatalog</c>); aqui fica o estado ligado/desligado e uma cópia do nome/descrição para o registro.
/// </summary>
public class AiFeature : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public bool RunsOnDevice { get; set; }
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// Registro de UMA chamada de IA (ou de um uso no aparelho, como o "Tirar o fundo"): quem, home game, recurso, modelo,
/// tokens, custo e resultado. NUNCA guarda o conteúdo enviado nem o recebido.
/// </summary>
public class AiUsage : BaseEntity
{
    public string FeatureKey { get; set; } = string.Empty;
    /// <summary>Modelo da chamada; null no uso feito no aparelho.</summary>
    public string? Model { get; set; }
    public bool OnDevice { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>Nome de quem usou (conta ou dealer do QR) no momento do uso.</summary>
    public string? ActorName { get; set; }
    public Guid? HomeGameId { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheCreationInputTokens { get; set; }
    public long CacheReadInputTokens { get; set; }
    public decimal CostUsd { get; set; }
    /// <summary>AiUsageOutcome (Success | Error | Refused), gravado como texto.</summary>
    public string Outcome { get; set; } = string.Empty;
    /// <summary>Categoria do erro/recusa (ex.: "unauthorized", "rate_limited", "monthly_limit").</summary>
    public string? ErrorCode { get; set; }
    /// <summary>Mensagem em pt-BR mostrada ao Admin (nunca o conteúdo da chamada).</summary>
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }
}
