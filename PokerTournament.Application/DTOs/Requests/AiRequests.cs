namespace PokerTournament.Application.DTOs.Requests;

// IA na administração (SPEC-20261027-1312): corpos de api/v1/admin/ai (só Admin) e de api/v1/ai (logado ou dealer do QR).

/// <summary><c>PUT admin/ai/settings</c>: liga/desliga geral, modelo padrão e limite mensal em US$ (0 = nenhum uso pago).</summary>
public class UpdateAiSettingsRequest
{
    public bool Enabled { get; set; }
    public string DefaultModel { get; set; } = string.Empty;
    public decimal MonthlyLimitUsd { get; set; }
}

/// <summary><c>PUT admin/ai/key</c>: cadastrar ou trocar a chave da Anthropic (guardada cifrada; nunca volta ao navegador).</summary>
public class SetAiKeyRequest
{
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary><c>POST admin/ai/key/test</c>: modelo a conferir (vazio = o modelo padrão).</summary>
public class TestAiKeyRequest
{
    public string? Model { get; set; }
}

/// <summary><c>PUT admin/ai/features/{key}</c>: liga/desliga um recurso.</summary>
public class SetAiFeatureRequest
{
    public bool Enabled { get; set; }
}

/// <summary>
/// <c>PUT admin/ai/prices/{model}</c>: preços em US$ por milhão de tokens (entrada, saída, escrita e leitura de cache) e,
/// para modelos com faixa longa (Haiku 5.5), os preços acima do limite de tokens do prompt.
/// </summary>
public class UpdateAiPriceRequest
{
    public decimal InputPerMTok { get; set; }
    public decimal OutputPerMTok { get; set; }
    public decimal CacheWritePerMTok { get; set; }
    public decimal CacheReadPerMTok { get; set; }
    public decimal? LongInputPerMTok { get; set; }
    public decimal? LongOutputPerMTok { get; set; }
    public decimal? LongCacheWritePerMTok { get; set; }
    public decimal? LongCacheReadPerMTok { get; set; }
}

/// <summary>
/// <c>POST ai/usage</c>: uso de um recurso que roda no aparelho (hoje o "Tirar o fundo"), avisado pelo app a cada foto
/// salva com o fundo tirado. <see cref="PersonId"/>/<see cref="TournamentId"/> (opcionais) dizem de qual home game é
/// a foto; o servidor só aceita o que a conta (ou o dealer do QR) acessa.
/// </summary>
public class RecordAiUsageRequest
{
    public string Feature { get; set; } = string.Empty;
    public Guid? PersonId { get; set; }
    public Guid? TournamentId { get; set; }
}
