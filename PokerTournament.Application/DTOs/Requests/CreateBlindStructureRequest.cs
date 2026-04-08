namespace PokerTournament.Application.DTOs.Requests;

public class CreateBlindStructureRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? HomeGameId { get; set; }
    public List<BlindLevelItem> Levels { get; set; } = [];

    // Template defaults
    public decimal? DefaultBuyIn { get; set; }
    public decimal? DefaultRebuy { get; set; }
    public decimal? DefaultAddon { get; set; }
    public int? DefaultStartingStack { get; set; }
    public int? DefaultRebuyStack { get; set; }
    public int? DefaultAddonStack { get; set; }
    public int? DefaultMaxRebuys { get; set; }
    public bool? DefaultAddonAllowed { get; set; }
    public bool? DefaultAddonDoubleAllowed { get; set; }
    public int? DefaultLateRegistrationLevel { get; set; }
    public int? DefaultRebuyUntilLevel { get; set; }
    public int? DefaultSeatsPerTable { get; set; }

    public class BlindLevelItem
    {
        public int LevelNumber { get; set; }
        public int SmallBlind { get; set; }
        public int BigBlind { get; set; }
        public int Ante { get; set; }
        public int BigBlindAnte { get; set; }
        public int DurationMinutes { get; set; }
        public bool IsBreak { get; set; }
        public string? BreakDescription { get; set; }
    }
}
