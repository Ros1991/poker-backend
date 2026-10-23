namespace PokerTournament.Application.DTOs.Responses;

public class QuickAddEntryResponse
{
    public EntryResponse Entry { get; set; } = null!;
    // Onde o jogador sentou (null = ainda sem mesa: não há mesas ou não há lugar livre)
    public Guid? TableId { get; set; }
    public string? TableName { get; set; }
    public int? SeatNumber { get; set; }
}
