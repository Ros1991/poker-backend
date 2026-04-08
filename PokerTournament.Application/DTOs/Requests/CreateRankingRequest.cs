namespace PokerTournament.Application.DTOs.Requests;

public class CreateRankingRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Season { get; set; }
    public Guid? ScoringRuleId { get; set; }
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public string ScoringMode { get; set; } = "Formula"; // Formula ou Table
    public string? ScoringFormula { get; set; }
    public string? ScoringTable { get; set; } // JSON: [{"position":1,"points":23},...]
    public decimal? AccumulatedPrize { get; set; }
}
