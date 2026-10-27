namespace PokerTournament.Application.DTOs.Responses;

public class EliminationResultResponse
{
    public int Position { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string? EliminatedBy { get; set; }
    public int PlayersRemaining { get; set; }
    public bool IsInTheMoney { get; set; }
    public string? WhatsAppLink { get; set; }

    // Knockout (SPEC-20261026-1909): bounty que saiu da cabeça do eliminado — em dinheiro para quem eliminou e, no PKO,
    // o que somou na cabeça dele. Nulos no torneio Normal.
    public decimal? BountyAmount { get; set; }
    public decimal? BountyCash { get; set; }
    public decimal? BountyToHead { get; set; }

    // Revisão geral 2: a eliminação que deixou só o campeão finaliza o torneio sozinha — a tela avisa no toast
    public bool TournamentFinished { get; set; }
    /// <summary>Apelido (ou o nome completo) do campeão, quando o torneio se finalizou nesta eliminação</summary>
    public string? ChampionName { get; set; }
}
