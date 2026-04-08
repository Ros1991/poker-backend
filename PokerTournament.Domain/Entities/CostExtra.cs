namespace PokerTournament.Domain.Entities;

public class CostExtra : BaseEntity
{
    public Guid TournamentId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Beneficiary { get; set; }
    public string? PixKey { get; set; }
    public string? PixKeyType { get; set; }
    public bool IsCashBox { get; set; }
    public decimal PaidAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; } // Pix, Dinheiro, Caixa
    public DateTimeOffset? PaidAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Tournament Tournament { get; set; } = null!;
}
