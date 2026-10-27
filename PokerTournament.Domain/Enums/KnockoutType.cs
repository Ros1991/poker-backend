namespace PokerTournament.Domain.Enums;

/// <summary>
/// Formato do torneio quanto a bounties (SPEC-20261026-1909), gravado como string via <c>nameof</c>
/// (<c>Tournament.KnockoutType</c> e <c>BlindStructure.DefaultKnockoutType</c>).
/// </summary>
public enum KnockoutType
{
    /// <summary>Torneio normal: sem bounty (nada muda para quem não usa knockout).</summary>
    None,
    /// <summary>Knockout simples: quem elimina leva 100% do bounty atual do eliminado.</summary>
    Simple,
    /// <summary>Knockout progressivo (PKO): quem elimina leva 50% em dinheiro e 50% somam na própria cabeça.</summary>
    Progressive,
}
