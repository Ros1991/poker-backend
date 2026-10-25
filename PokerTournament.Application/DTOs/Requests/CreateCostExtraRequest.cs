namespace PokerTournament.Application.DTOs.Requests;

public class CreateCostExtraRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Beneficiary { get; set; }
    public string? PixKey { get; set; }
    // SPEC-20261023-1218: tipo ESCOLHIDO na tela (null = detectar); o gravado é sempre o tipo da chave normalizada
    public string? PixKeyType { get; set; }
    public bool IsCashBox { get; set; }
}
