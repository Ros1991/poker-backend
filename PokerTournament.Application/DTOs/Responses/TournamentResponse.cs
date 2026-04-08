namespace PokerTournament.Application.DTOs.Responses;

public class TournamentResponse
{
    public Guid Id { get; set; }
    public Guid HomeGameId { get; set; }
    public Guid? RankingId { get; set; }
    public Guid? BlindStructureId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset Date { get; set; }
    public TimeSpan? StartTime { get; set; }
    public string Status { get; set; } = string.Empty;

    public decimal BuyInAmount { get; set; }
    public decimal? RebuyAmount { get; set; }
    public decimal? AddonAmount { get; set; }

    public int StartingStack { get; set; }
    public int? RebuyStack { get; set; }
    public int? AddonStack { get; set; }

    public int? MaxRebuys { get; set; }
    public bool AddonAllowed { get; set; }
    public bool AddonDoubleAllowed { get; set; }
    public int? LateRegistrationLevel { get; set; }
    public int? RebuyUntilLevel { get; set; }
    public int SeatsPerTable { get; set; } = 9;

    public int? CurrentLevel { get; set; }
    public bool IsTimerRunning { get; set; }

    public decimal TotalPrizePool { get; set; }
    public decimal TotalCosts { get; set; }
    public decimal NetPrizePool { get; set; }
    public bool PrizeConfirmed { get; set; }

    public bool SettlementClosed { get; set; }
    public DateTimeOffset? SettlementClosedAt { get; set; }

    public int TotalEntries { get; set; }
    public int TotalRebuys { get; set; }
    public int TotalAddons { get; set; }
    public int PlayersRemaining { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
