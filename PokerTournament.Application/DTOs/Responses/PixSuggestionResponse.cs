namespace PokerTournament.Application.DTOs.Responses;

public class PixSuggestionResponse
{
    public Guid CostExtraId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal PendingAmount { get; set; }
    public string? PixKey { get; set; }
    public string? Beneficiary { get; set; }
    public decimal MatchScore { get; set; }
    public decimal SuggestedPixAmount { get; set; }
}
