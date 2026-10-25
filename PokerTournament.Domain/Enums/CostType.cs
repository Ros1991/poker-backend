namespace PokerTournament.Domain.Enums;

/// <summary>
/// Tipo do custo do torneio (<c>CostExtra.CostType</c>, gravado como string via <c>nameof</c>).
/// SPEC-20261023-1215 (CP2): usado só no código novo; as strings antigas ("Manual", "Staff",
/// "RankingAccumulated") continuam como estão.
/// </summary>
public enum CostType
{
    /// <summary>Lançado à mão na aba Custos.</summary>
    Manual,
    /// <summary>Staff automático (cresce por entrada; não removível).</summary>
    Staff,
    /// <summary>Acumulado do ranking (automático).</summary>
    RankingAccumulated,
    /// <summary>Veio dos custos padrão da estrutura de blinds (inclui o custo de dealer). Comporta-se como Manual.</summary>
    Default,
}
