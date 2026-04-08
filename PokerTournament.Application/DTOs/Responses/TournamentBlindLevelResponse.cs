namespace PokerTournament.Application.DTOs.Responses;

public class TournamentBlindLevelResponse
{
    public Guid Id { get; set; }
    public int LevelNumber { get; set; }
    public int SmallBlind { get; set; }
    public int BigBlind { get; set; }
    public int Ante { get; set; }
    public int BigBlindAnte { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsBreak { get; set; }
    public string? BreakDescription { get; set; }
}
