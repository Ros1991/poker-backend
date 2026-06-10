namespace PokerTournament.Application.DTOs.Requests;

public class BulkCreateEntriesRequest
{
    public List<Guid> PersonIds { get; set; } = [];
    public bool BuyInPaid { get; set; }
}
