namespace PokerTournament.Application.DTOs.Responses;

// IA na administração (SPEC-20261027-1312). A chave da API NUNCA aparece aqui: só a dica "sk-ant-…ABCD".

/// <summary><c>GET admin/ai</c>: tudo o que a tela "Inteligência artificial" mostra.</summary>
public class AiOverviewResponse
{
    public bool Enabled { get; set; }
    public string DefaultModel { get; set; } = string.Empty;
    public decimal MonthlyLimitUsd { get; set; }
    /// <summary>AI_SECRETS_KEY presente e válida: sem ela a tela avisa e não deixa salvar a chave.</summary>
    public bool SecretsKeyConfigured { get; set; }
    public AiKeyInfoResponse Key { get; set; } = new();
    public AiKeyTestInfoResponse? LastTest { get; set; }
    public AiMonthResponse Month { get; set; } = new();
    public List<AiFeatureResponse> Features { get; set; } = [];
    public List<AiModelPriceResponse> Prices { get; set; } = [];
    public List<AiUsageSummaryResponse> UsageByFeature { get; set; } = [];
    public List<AiUsageSummaryResponse> UsageByModel { get; set; } = [];
    public List<AiUsageItemResponse> RecentCalls { get; set; } = [];
}

public class AiKeyInfoResponse
{
    public bool Configured { get; set; }
    /// <summary>"sk-ant-…ABCD" (4 últimos); null sem chave.</summary>
    public string? Hint { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }
    /// <summary>A chave existe mas não decifra com a AI_SECRETS_KEY atual (precisa cadastrar de novo).</summary>
    public bool Unreadable { get; set; }
}

public class AiKeyTestInfoResponse
{
    public DateTimeOffset At { get; set; }
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Model { get; set; }
}

/// <summary>Gasto do mês corrente (UTC) contra o limite; aviso a partir de 80 %.</summary>
public class AiMonthResponse
{
    public DateTimeOffset Start { get; set; }
    public decimal SpentUsd { get; set; }
    public decimal LimitUsd { get; set; }
    /// <summary>Gasto / limite × 100 (null com limite 0).</summary>
    public decimal? Percent { get; set; }
    /// <summary>Gasto em 80 % do limite ou mais.</summary>
    public bool Warning { get; set; }
    /// <summary>Chamadas pagas recusadas agora (limite 0 ou atingido).</summary>
    public bool Blocked { get; set; }
}

public class AiFeatureResponse
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public bool RunsOnDevice { get; set; }
    public bool Enabled { get; set; }
    /// <summary>Usos (no aparelho) ou chamadas (pagos) no mês.</summary>
    public int MonthUses { get; set; }
}

public class AiModelPriceResponse
{
    public string Model { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal InputPerMTok { get; set; }
    public decimal OutputPerMTok { get; set; }
    public decimal CacheWritePerMTok { get; set; }
    public decimal CacheReadPerMTok { get; set; }
    public int? LongContextThresholdTokens { get; set; }
    public decimal? LongInputPerMTok { get; set; }
    public decimal? LongOutputPerMTok { get; set; }
    public decimal? LongCacheWritePerMTok { get; set; }
    public decimal? LongCacheReadPerMTok { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>Uso do mês agrupado por recurso ou por modelo.</summary>
public class AiUsageSummaryResponse
{
    /// <summary>Chave do recurso ou id do modelo ("no-aparelho" para o uso sem modelo).</summary>
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Chamadas feitas à Anthropic (ok + erro) ou usos no aparelho.</summary>
    public int Calls { get; set; }
    public int Errors { get; set; }
    /// <summary>Recusadas antes de chamar (desligado, sem chave, limite).</summary>
    public int Refused { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheCreationInputTokens { get; set; }
    public long CacheReadInputTokens { get; set; }
    public decimal CostUsd { get; set; }
}

/// <summary>Uma linha das últimas chamadas (sem o conteúdo enviado/recebido — ele nunca é guardado).</summary>
public class AiUsageItemResponse
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string? ActorName { get; set; }
    public string? HomeGameName { get; set; }
    public string FeatureKey { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? ModelName { get; set; }
    public bool OnDevice { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheCreationInputTokens { get; set; }
    public long CacheReadInputTokens { get; set; }
    public decimal CostUsd { get; set; }
    /// <summary>Success | Error | Refused</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }
}

/// <summary><c>POST admin/ai/key/test</c>: resultado do teste pela API de modelos.</summary>
public class AiKeyTestResponse
{
    public bool Ok { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

/// <summary><c>GET ai/features</c>: chaves dos recursos de IA que este aparelho pode usar agora.</summary>
public class AiAvailableFeaturesResponse
{
    public List<string> Features { get; set; } = [];
}
