namespace PokerTournament.Application.DTOs.Requests;

public class SettleAgainstCostRequest
{
    public Guid CostExtraId { get; set; }
    public decimal Amount { get; set; }
}
