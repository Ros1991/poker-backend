namespace PokerTournament.Application.DTOs.Requests;

public class CreatePaymentRequest
{
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public decimal? PixAmount { get; set; }
    public decimal? CashAmount { get; set; }
    public Guid? PixDestinationCostExtraId { get; set; }
    public string? Notes { get; set; }
}
