namespace PokerTournament.Application.DTOs.Responses;

public class FinancialSummaryResponse
{
    public IncomeBreakdown Income { get; set; } = new();
    public List<CostItem> Costs { get; set; } = [];
    public List<PrizeAllocation> Prizes { get; set; } = [];
    public List<PaymentItem> Payments { get; set; } = [];
}

public class IncomeBreakdown
{
    public decimal BuyIns { get; set; }
    public decimal Rebuys { get; set; }
    public decimal Addons { get; set; }
    public decimal Total { get; set; }
}

public class CostItem
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class PaymentItem
{
    public Guid Id { get; set; }
    public Guid? EntryId { get; set; }
    public string? PlayerName { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
