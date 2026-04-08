namespace PokerTournament.Domain.Entities;

public class TournamentEntry : BaseEntity
{
    public Guid TournamentId { get; set; }
    public Guid PersonId { get; set; }
    public Guid? TableId { get; set; }
    public int? SeatNumber { get; set; }
    public string Status { get; set; } = string.Empty;

    // Buy-in
    public bool BuyInPaid { get; set; }
    public decimal BuyInAmount { get; set; }

    // Rebuy
    public int RebuyCount { get; set; }
    public decimal RebuyTotal { get; set; }

    // Addon
    public bool AddonPurchased { get; set; }
    public bool AddonDouble { get; set; }
    public decimal AddonAmount { get; set; }

    // Totals
    public decimal TotalDue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Balance { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;

    // Elimination
    public int? FinalPosition { get; set; }
    public DateTimeOffset? EliminatedAt { get; set; }
    public Guid? EliminatedById { get; set; }
    public Guid? EliminationTableId { get; set; }
    public int? EliminationSeatNumber { get; set; }

    // Re-entry
    public bool IsReentry { get; set; }
    public Guid? OriginalEntryId { get; set; }
    public int EntryNumber { get; set; } = 1;

    // Prize
    public decimal? PrizeAmount { get; set; }
    public bool PrizePaid { get; set; }

    // Ranking
    public decimal? RankingPoints { get; set; }

    // Registration
    public DateTimeOffset RegisteredAt { get; set; }
    public Guid? RegisteredBy { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Tournament Tournament { get; set; } = null!;
    public Person Person { get; set; } = null!;
    public TournamentTable? Table { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = [];
    public TournamentEntry? EliminatedByEntry { get; set; }
}
