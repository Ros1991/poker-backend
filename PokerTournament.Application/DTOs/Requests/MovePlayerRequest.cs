namespace PokerTournament.Application.DTOs.Requests;

public class MovePlayerRequest
{
    public Guid EntryId { get; set; }
    public Guid ToTableId { get; set; }
    public int ToSeat { get; set; }
    // Opcional: "balance" quando a mudança veio do aviso de mesas desequilibradas (só muda o texto do histórico)
    public string? Reason { get; set; }
}
