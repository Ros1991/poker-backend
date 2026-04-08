namespace PokerTournament.Domain.Entities;

public class Ranking : SoftDeletableEntity
{
    public Guid HomeGameId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Season { get; set; }
    public Guid? ScoringRuleId { get; set; }
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string ScoringMode { get; set; } = "Formula"; // Formula ou Table
    public string? ScoringFormula { get; set; }
    public string? ScoringTable { get; set; } // JSON: [{"position":1,"points":23},...]
    public decimal AccumulatedPrize { get; set; } // Total acumulado para premiação do ranking
    public int DiscardCount { get; set; } = 0; // Piores resultados que cada jogador pode descartar

    // Navigation
    public HomeGame HomeGame { get; set; } = null!;
    public ScoringRule? ScoringRule { get; set; }
    public ICollection<RankingScore> Scores { get; set; } = [];
    public ICollection<Tournament> Tournaments { get; set; } = [];
}
