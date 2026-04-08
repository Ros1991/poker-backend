namespace PokerTournament.Domain.Entities;

public class HomeGame : SoftDeletableEntity
{
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? LogoUrl { get; set; }
    public string? PixKey { get; set; }
    public string? PixBeneficiary { get; set; }
    public string Timezone { get; set; } = "America/Sao_Paulo";
    public decimal? DefaultBuyIn { get; set; }
    public decimal? DefaultRebuy { get; set; }
    public decimal? DefaultAddon { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public User Owner { get; set; } = null!;
    public ICollection<HomeGameMember> Members { get; set; } = [];
    public ICollection<Person> Persons { get; set; } = [];
    public ICollection<Ranking> Rankings { get; set; } = [];
    public ICollection<Tournament> Tournaments { get; set; } = [];
}
