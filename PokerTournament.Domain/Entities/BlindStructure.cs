namespace PokerTournament.Domain.Entities;

public class BlindStructure : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? HomeGameId { get; set; }
    public bool IsActive { get; set; } = true;

    // Template defaults (pre-fill tournament when selecting this structure)
    public decimal? DefaultBuyIn { get; set; }
    public decimal? DefaultRebuy { get; set; }
    public decimal? DefaultAddon { get; set; }
    public int? DefaultStartingStack { get; set; }
    public int? DefaultRebuyStack { get; set; }
    public int? DefaultAddonStack { get; set; }
    public int? DefaultMaxRebuys { get; set; }
    public bool DefaultAddonAllowed { get; set; } = true;
    public bool DefaultAddonDoubleAllowed { get; set; }
    public int? DefaultLateRegistrationLevel { get; set; }
    public int? DefaultRebuyUntilLevel { get; set; }
    public int? DefaultSeatsPerTable { get; set; }

    // Navigation
    public HomeGame? HomeGame { get; set; }
    public ICollection<BlindLevel> Levels { get; set; } = [];
}
