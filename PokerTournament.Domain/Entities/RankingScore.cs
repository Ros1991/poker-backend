namespace PokerTournament.Domain.Entities;

public class RankingScore : BaseEntity
{
    public Guid RankingId { get; set; }
    public Guid PersonId { get; set; }
    public Guid TournamentId { get; set; }
    public Guid? EntryId { get; set; }
    public int? Position { get; set; }
    public decimal Points { get; set; }
    public decimal BonusPoints { get; set; }
    public decimal TotalPoints { get; set; }
    public int PlayerCount { get; set; }

    // Navigation
    public Ranking Ranking { get; set; } = null!;
    public Person Person { get; set; } = null!;
    public Tournament Tournament { get; set; } = null!;
    public TournamentEntry? Entry { get; set; }
}
