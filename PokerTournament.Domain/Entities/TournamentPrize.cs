namespace PokerTournament.Domain.Entities;

public class TournamentPrize : BaseEntity
{
    public Guid TournamentId { get; set; }
    public int Position { get; set; }
    public decimal Amount { get; set; }
    public decimal? Percentage { get; set; }
    public Guid? EntryId { get; set; }
    public bool Paid { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaymentMethod { get; set; }

    // Navigation
    public Tournament Tournament { get; set; } = null!;
    public TournamentEntry? Entry { get; set; }
}
