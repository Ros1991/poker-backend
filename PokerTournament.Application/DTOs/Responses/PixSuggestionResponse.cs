namespace PokerTournament.Application.DTOs.Responses;

public class PixSuggestionResponse
{
    public Guid CostExtraId { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>O que o destino ainda tem a receber (Amount − PaidAmount); o pagamento nunca passa disso (SPEC-20261026-1906)</summary>
    public decimal PendingAmount { get; set; }
    public string? PixKey { get; set; }
    public string? Beneficiary { get; set; }
    public decimal MatchScore { get; set; }
    public decimal SuggestedPixAmount { get; set; }
    /// <summary>Custo marcado como Caixa (gestor financeiro): sem limite, vem primeiro na lista</summary>
    public bool IsCashBox { get; set; }
}
