namespace PokerTournament.Domain.Entities;

public class Tournament : SoftDeletableEntity
{
    public Guid HomeGameId { get; set; }
    public Guid? RankingId { get; set; }
    public Guid? BlindStructureId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset Date { get; set; }
    public TimeSpan? StartTime { get; set; }
    public string Status { get; set; } = string.Empty;

    // Buy-in / Rebuy / Addon amounts (money)
    public decimal BuyInAmount { get; set; }
    public decimal? RebuyAmount { get; set; }
    public decimal? AddonAmount { get; set; }

    // Chip stacks
    public int StartingStack { get; set; }
    public int? RebuyStack { get; set; }
    public int? AddonStack { get; set; }

    // Rebuy / Addon rules
    public int? MaxRebuys { get; set; }
    public bool AddonAllowed { get; set; }
    public bool AddonDoubleAllowed { get; set; }
    public int? LateRegistrationLevel { get; set; }
    public int? RebuyUntilLevel { get; set; }

    // Mesa
    public int SeatsPerTable { get; set; } = 9;

    // Timer state
    public int? CurrentLevel { get; set; }
    public DateTimeOffset? TimerStartedAt { get; set; }
    public DateTimeOffset? TimerPausedAt { get; set; }
    public int TimerElapsedSeconds { get; set; }
    public bool IsTimerRunning { get; set; }

    // Prize pool
    public decimal TotalPrizePool { get; set; }
    public decimal TotalCosts { get; set; }
    public decimal NetPrizePool { get; set; }
    public bool PrizeConfirmed { get; set; }
    public DateTimeOffset? PrizeConfirmedAt { get; set; }

    // Settlement
    public bool SettlementClosed { get; set; }
    public DateTimeOffset? SettlementClosedAt { get; set; }
    public Guid? SettlementClosedBy { get; set; }

    // Counters
    public int TotalEntries { get; set; }
    public int TotalRebuys { get; set; }
    public int TotalAddons { get; set; }
    public int PlayersRemaining { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CreatedBy { get; set; }

    // Navigation
    public HomeGame HomeGame { get; set; } = null!;
    public Ranking? Ranking { get; set; }
    public BlindStructure? BlindStructure { get; set; }
    public ICollection<TournamentEntry> Entries { get; set; } = [];
    public ICollection<TournamentTable> Tables { get; set; } = [];
    public ICollection<TournamentPrize> Prizes { get; set; } = [];
    public ICollection<CostExtra> CostExtras { get; set; } = [];
    public ICollection<Elimination> Eliminations { get; set; } = [];
    public ICollection<TournamentDealer> Dealers { get; set; } = [];
    public ICollection<TournamentBlindLevel> BlindLevels { get; set; } = [];
}
