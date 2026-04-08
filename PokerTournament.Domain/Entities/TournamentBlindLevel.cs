namespace PokerTournament.Domain.Entities;

public class TournamentBlindLevel : BaseEntity
{
    public Guid TournamentId { get; set; }
    public int LevelNumber { get; set; }
    public int SmallBlind { get; set; }
    public int BigBlind { get; set; }
    public int Ante { get; set; }
    public int BigBlindAnte { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsBreak { get; set; }
    public string? BreakDescription { get; set; }

    public Tournament Tournament { get; set; } = null!;
}
