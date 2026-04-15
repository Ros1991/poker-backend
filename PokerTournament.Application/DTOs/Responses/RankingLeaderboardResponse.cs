namespace PokerTournament.Application.DTOs.Responses;

public class RankingLeaderboardResponse
{
    public int Position { get; set; }
    public PersonResponse Person { get; set; } = null!;
    public decimal TotalPoints { get; set; }
    public int TournamentsPlayed { get; set; }
    public int? BestPosition { get; set; }
    public decimal DiscardedPoints { get; set; }
    public decimal AveragePoints { get; set; }
}
