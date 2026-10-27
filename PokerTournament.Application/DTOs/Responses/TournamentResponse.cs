namespace PokerTournament.Application.DTOs.Responses;

public class TournamentResponse
{
    public Guid Id { get; set; }
    public Guid HomeGameId { get; set; }
    public string? HomeGameName { get; set; }
    public Guid? RankingId { get; set; }
    public string? RankingName { get; set; }
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

    // Knockout (SPEC-20261026-1909): None | Simple | Progressive; BountyAmount null no Normal
    public string KnockoutType { get; set; } = "None";
    public decimal? BountyAmount { get; set; }

    public int? CurrentLevel { get; set; }
    public bool IsTimerRunning { get; set; }

    public decimal TotalPrizePool { get; set; }
    public decimal TotalCosts { get; set; }
    public decimal NetPrizePool { get; set; }
    /// <summary>Bounties comprados (inscrições + rebuys), à parte do prize pool: NetPrizePool já vem sem eles.</summary>
    public decimal TotalBounties { get; set; }
    public bool PrizeConfirmed { get; set; }

    public bool SettlementClosed { get; set; }
    public DateTimeOffset? SettlementClosedAt { get; set; }

    public int TotalEntries { get; set; }
    public int TotalRebuys { get; set; }
    public int TotalAddons { get; set; }
    public int PlayersRemaining { get; set; }

    public int? PunctualityBonusCount { get; set; }
    public int? PunctualityBonusChips { get; set; }

    public string? Notes { get; set; }
    public string? ResponsiblePixKey { get; set; }
    public decimal? StaffAmount { get; set; }
    public string? RankingContribMode { get; set; }
    public decimal? RankingContribValue { get; set; }
    public decimal? RankingPrizeAccrued { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Plano EFETIVO do dono do home game (SPEC-20261023-1205) e os recursos liberados neste torneio
    // (cadeados das abas, terminal do dealer). Preenchidos pelo TournamentService depois do Map.
    public string OwnerPlan { get; set; } = "Mesa";
    public List<string> PlanFeatures { get; set; } = [];
}
