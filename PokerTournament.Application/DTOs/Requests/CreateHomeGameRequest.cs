namespace PokerTournament.Application.DTOs.Requests;

public class CreateHomeGameRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? PixKey { get; set; }
    public string? PixBeneficiary { get; set; }
    public decimal? DefaultBuyIn { get; set; }
    public decimal? DefaultRebuy { get; set; }
    public decimal? DefaultAddon { get; set; }
}
