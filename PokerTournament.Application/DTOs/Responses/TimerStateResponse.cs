namespace PokerTournament.Application.DTOs.Responses;

public class TimerStateResponse
{
    public int? CurrentLevel { get; set; }
    public int SmallBlind { get; set; }
    public int BigBlind { get; set; }
    public int Ante { get; set; }
    public int DurationMinutes { get; set; }
    public int ElapsedSeconds { get; set; }
    public int RemainingSeconds { get; set; }
    public bool IsRunning { get; set; }
    public bool IsBreak { get; set; }
    public NextLevelInfo? NextLevel { get; set; }
}

public class NextLevelInfo
{
    public int LevelNumber { get; set; }
    public int SmallBlind { get; set; }
    public int BigBlind { get; set; }
    public int Ante { get; set; }
    public bool IsBreak { get; set; }
}
