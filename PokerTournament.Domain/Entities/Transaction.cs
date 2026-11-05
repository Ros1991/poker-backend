namespace PokerTournament.Domain.Entities;

public class Transaction : BaseEntity
{
    public Guid TournamentId { get; set; }
    public Guid? EntryId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal? PixAmount { get; set; }
    public decimal? CashAmount { get; set; }
    public Guid? PixDestinationId { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public Guid? ReversedBy { get; set; }
    public string? ReversalReason { get; set; }
    // Rebuy/add-on lançado fora do período, com a confirmação de quem lançou (SPEC-20261105-1707): depois do "rebuy até o
    // nível N" ou no intervalo de acerto. Só marca o histórico ("fora do período"); o dinheiro conta igual.
    public bool OutsideWindow { get; set; }

    // Navigation
    public Tournament Tournament { get; set; } = null!;
    public TournamentEntry? Entry { get; set; }
    public CostExtra? PixDestination { get; set; }
}
