namespace PokerTournament.Domain.Enums;

/// <summary>
/// Resultado de um registro de uso de IA (<c>AiUsage.Outcome</c>, gravado como string via <c>nameof</c>) —
/// SPEC-20261027-1312.
/// </summary>
public enum AiUsageOutcome
{
    /// <summary>Chamada respondida (ou uso no aparelho concluído).</summary>
    Success,
    /// <summary>A Anthropic (ou a rede) devolveu erro; a resposta recusada pelo modelo também entra aqui.</summary>
    Error,
    /// <summary>Recusada ANTES de chamar: IA ou recurso desligados, sem chave, limite do mês.</summary>
    Refused,
}
