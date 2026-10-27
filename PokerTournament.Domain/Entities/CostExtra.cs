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
    /// <summary>
    /// Quanto o CAIXA entregou a este custo (botão "Pagar" da aba Custos), guardado à parte do que os jogadores pagaram
    /// direto (revisão geral 2): o pago do custo = mín(valor, créditos dos jogadores + isto). Custo que encolhe não perde
    /// esta parte (o excedente aparece como "recebidos a mais — acertar com o caixa") e volta a valer quando ele cresce.
    /// </summary>
    public decimal PaidByCashBox { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; } // Pix, Dinheiro, Caixa
    public DateTimeOffset? PaidAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? Notes { get; set; }
    // Tipo: Manual | Staff | RankingAccumulated | Default (enum CostType; Staff/Ranking são auto-gerados;
    // Default veio dos custos padrão da estrutura de blinds e se comporta como Manual — SPEC-20261023-1215)
    public string CostType { get; set; } = "Manual";

    // Navigation
    public Tournament Tournament { get; set; } = null!;
}
