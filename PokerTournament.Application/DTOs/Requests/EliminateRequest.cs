namespace PokerTournament.Application.DTOs.Requests;

public class EliminateRequest
{
    public Guid? EliminatedByEntryId { get; set; }
    public string? Notes { get; set; }
}
