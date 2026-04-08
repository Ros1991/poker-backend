namespace PokerTournament.Application.DTOs.Responses;

public class HomeGameResponse
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? LogoUrl { get; set; }
    public string? PixKey { get; set; }
    public string? PixBeneficiary { get; set; }
    public string Timezone { get; set; } = string.Empty;
    public decimal? DefaultBuyIn { get; set; }
    public decimal? DefaultRebuy { get; set; }
    public decimal? DefaultAddon { get; set; }
    public bool IsActive { get; set; }
    public int MemberCount { get; set; }
    public int TournamentCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
