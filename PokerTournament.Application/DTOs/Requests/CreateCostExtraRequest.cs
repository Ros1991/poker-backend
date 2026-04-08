namespace PokerTournament.Application.DTOs.Requests;

public class CreateCostExtraRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Beneficiary { get; set; }
    public string? PixKey { get; set; }
    public string? PixKeyType { get; set; }
    public bool IsCashBox { get; set; }
}
