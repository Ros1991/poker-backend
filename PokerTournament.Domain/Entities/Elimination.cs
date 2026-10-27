namespace PokerTournament.Domain.Entities;

public class Elimination : BaseEntity
{
    public Guid TournamentId { get; set; }
    public Guid EntryId { get; set; }
    public Guid? EliminatedByEntryId { get; set; }
    public Guid? TableId { get; set; }
    public int? Position { get; set; }
    public int? BlindLevel { get; set; }
    public DateTimeOffset EliminatedAt { get; set; }
    public string? Notes { get; set; }
    public Guid? CreatedBy { get; set; }
    public bool Corrected { get; set; }
    public DateTimeOffset? CorrectedAt { get; set; }
    public Guid? CorrectedBy { get; set; }
    public string? CorrectionReason { get; set; }

    // Bounty transferido nesta eliminação (SPEC-20261026-1909; nulos fora do knockout): BountyAmount = a cabeça inteira
    // do eliminado; BountyCash = o que foi em dinheiro para quem eliminou; BountyToHead = o que somou na cabeça de quem
    // eliminou (só no PKO). BountyCash + BountyToHead = BountyAmount. O desfazer devolve exatamente estes valores.
    public decimal? BountyAmount { get; set; }
    public decimal? BountyCash { get; set; }
    public decimal? BountyToHead { get; set; }

    // Navigation
    public Tournament Tournament { get; set; } = null!;
    public TournamentEntry Entry { get; set; } = null!;
    public TournamentEntry? EliminatedByEntry { get; set; }
    public TournamentTable? Table { get; set; }
}
