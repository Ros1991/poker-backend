namespace PokerTournament.Application.DTOs.Responses;

public class PaymentSummaryResponse
{
    public decimal TotalDue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalPending { get; set; }
    public int PaidCount { get; set; }
    public int PendingCount { get; set; }
    public List<EntryPaymentInfo> Entries { get; set; } = [];
}

public class EntryPaymentInfo
{
    public Guid EntryId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public decimal TotalDue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Balance { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}
