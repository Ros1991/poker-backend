namespace PokerTournament.Application.DTOs.Requests;

public class DrawTablesRequest
{
    public int? TableCount { get; set; }
    public int? MaxSeatsPerTable { get; set; }
}

public class SetTableDealerRequest
{
    public Guid? DealerPersonId { get; set; }
}
