namespace PokerTournament.Application.DTOs.Responses;

public class BreakTableResponse
{
    public string BrokenTableName { get; set; } = string.Empty;
    public List<BreakTableMove> Moves { get; set; } = [];
    // true quando sobrou 1 mesa ativa (virou a mesa final)
    public bool FinalTable { get; set; }
}

public class BreakTableMove
{
    public Guid EntryId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public Guid ToTableId { get; set; }
    public string ToTableName { get; set; } = string.Empty;
    public int ToSeat { get; set; }
}
