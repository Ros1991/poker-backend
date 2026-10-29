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
    /// <summary>
    /// Home game principal da conta (SPEC-20261029-1723): um por dono, criado junto da conta (ou pela migration
    /// HomeGamePrincipal) e nunca excluído. No Mesa e no Clube é o único — a interface não fala em "home game": a conta
    /// é o clube, e no Clube o <see cref="Name"/> é o "nome do clube". Só a Liga cria outros (até 3 com o principal).
    /// </summary>
    public bool IsPrimary { get; set; }

    // Navigation
    public User Owner { get; set; } = null!;
    public ICollection<HomeGameMember> Members { get; set; } = [];
    public ICollection<Person> Persons { get; set; } = [];
    public ICollection<Ranking> Rankings { get; set; } = [];
    public ICollection<Tournament> Tournaments { get; set; } = [];
}
