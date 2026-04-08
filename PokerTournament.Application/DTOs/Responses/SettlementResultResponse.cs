namespace PokerTournament.Application.DTOs.Responses;

public class SettlementResultResponse
{
    public DateTimeOffset ClosedAt { get; set; }
    public int TotalPlayers { get; set; }
    public decimal TotalDue { get; set; }
    public decimal TotalPaid { get; set; }
    public int PendingPlayers { get; set; }
    public List<WhatsAppLinkItem> WhatsAppLinks { get; set; } = [];
}

public class WhatsAppLinkItem
{
    public Guid PersonId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Link { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
