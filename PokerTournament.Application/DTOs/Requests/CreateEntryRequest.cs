namespace PokerTournament.Application.DTOs.Requests;

public class CreateEntryRequest
{
    public Guid PersonId { get; set; }
    public bool BuyInPaid { get; set; }
}
