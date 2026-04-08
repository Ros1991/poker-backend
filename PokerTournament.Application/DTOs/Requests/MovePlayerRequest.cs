namespace PokerTournament.Application.DTOs.Requests;

public class MovePlayerRequest
{
    public Guid EntryId { get; set; }
    public Guid ToTableId { get; set; }
    public int ToSeat { get; set; }
}
