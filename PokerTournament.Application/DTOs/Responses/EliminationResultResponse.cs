namespace PokerTournament.Application.DTOs.Responses;

public class EliminationResultResponse
{
    public int Position { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string? EliminatedBy { get; set; }
    public int PlayersRemaining { get; set; }
    public bool IsInTheMoney { get; set; }
    public string? WhatsAppLink { get; set; }
}
