namespace PokerTournament.Application.DTOs.Responses;

public class EntryResponse
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public Guid PersonId { get; set; }
    public Guid? TableId { get; set; }
    public int? SeatNumber { get; set; }
    public string Status { get; set; } = string.Empty;

    public bool BuyInPaid { get; set; }
    public decimal BuyInAmount { get; set; }

    public int RebuyCount { get; set; }
    public decimal RebuyTotal { get; set; }

    public bool AddonPurchased { get; set; }
    public bool AddonDouble { get; set; }
    public decimal AddonAmount { get; set; }

    public decimal TotalDue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Balance { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;

    public int? FinalPosition { get; set; }
    public DateTimeOffset? EliminatedAt { get; set; }

    public bool IsReentry { get; set; }
    public int EntryNumber { get; set; }

    public decimal? PrizeAmount { get; set; }
    public bool PrizePaid { get; set; }
    public decimal? RankingPoints { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }
    public string? Notes { get; set; }

    public PersonResponse Person { get; set; } = null!;
}
