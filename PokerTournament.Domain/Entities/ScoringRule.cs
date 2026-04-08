namespace PokerTournament.Domain.Entities;

public class ScoringRule : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? HomeGameId { get; set; }
    public string PointsConfig { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Navigation
    public HomeGame? HomeGame { get; set; }
}
