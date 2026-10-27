namespace PokerTournament.Domain.Entities;

public class Tournament : SoftDeletableEntity
{
    public Guid HomeGameId { get; set; }
    public Guid? RankingId { get; set; }
    public Guid? BlindStructureId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset Date { get; set; }
    public TimeSpan? StartTime { get; set; }
    public string Status { get; set; } = string.Empty;

    // Buy-in / Rebuy / Addon amounts (money)
    public decimal BuyInAmount { get; set; }
    public decimal? RebuyAmount { get; set; }
    public decimal? AddonAmount { get; set; }

    // Chip stacks
    public int StartingStack { get; set; }
    public int? RebuyStack { get; set; }
    public int? AddonStack { get; set; }

    // Rebuy / Addon rules
    public int? MaxRebuys { get; set; }
    public bool AddonAllowed { get; set; }
    public bool AddonDoubleAllowed { get; set; }
    public int? LateRegistrationLevel { get; set; }
    public int? RebuyUntilLevel { get; set; }

    // Mesa
    public int SeatsPerTable { get; set; } = 9;

    // Knockout (SPEC-20261026-1909): None | Simple | Progressive (nome do enum KnockoutType). BountyAmount = parte
    // fixa do buy-in (e de cada rebuy) que vira o bounty do jogador; o resto vai para o prize pool. Add-on sem bounty.
    public string KnockoutType { get; set; } = nameof(Enums.KnockoutType.None);
    public decimal? BountyAmount { get; set; }

    // Timer state
    public int? CurrentLevel { get; set; }
    public DateTimeOffset? TimerStartedAt { get; set; }
    public DateTimeOffset? TimerPausedAt { get; set; }
    public int TimerElapsedSeconds { get; set; }
    public bool IsTimerRunning { get; set; }

    // Prize pool
    public decimal TotalPrizePool { get; set; }
    public decimal TotalCosts { get; set; }
    public decimal NetPrizePool { get; set; }
    // Bounties comprados nas inscrições e rebuys valendo (BountyAmount × (inscrições + rebuys)) — dinheiro à parte do
    // prize pool: NetPrizePool = TotalPrizePool − TotalCosts − TotalBounties (KnockoutRules.ApplyNet). 0 no Normal.
    public decimal TotalBounties { get; set; }
    public bool PrizeConfirmed { get; set; }
    public DateTimeOffset? PrizeConfirmedAt { get; set; }

    // Settlement
    public bool SettlementClosed { get; set; }
    public DateTimeOffset? SettlementClosedAt { get; set; }
    public Guid? SettlementClosedBy { get; set; }

    // Counters
    public int TotalEntries { get; set; }
    public int TotalRebuys { get; set; }
    public int TotalAddons { get; set; }
    public int PlayersRemaining { get; set; }
    // Já teve inscrição (gravado na primeira e nunca limpo). Desde a SPEC-20261026-1907 o limite semanal do plano
    // não usa mais este campo (cancelado e excluído não contam; o finalizado conta sempre — PlanService).
    public bool HadEntries { get; set; }

    public string? Notes { get; set; }
    // PIX do responsável pelo torneio no dia (opcional) — informado na criação
    public string? ResponsiblePixKey { get; set; }

    // Bônus de pontualidade (fichas extras pra quem chegou no horário)
    public int? PunctualityBonusCount { get; set; }  // nº de jogadores que receberam
    public int? PunctualityBonusChips { get; set; }  // fichas de bônus por jogador

    // Staff (comissão da casa) e contribuição ao ranking
    public decimal? StaffAmount { get; set; }         // valor de staff por jogador (por entrada)
    public string? RankingContribMode { get; set; }   // PerPlayer | Percent | null
    public decimal? RankingContribValue { get; set; } // valor por jogador OU percentual (10 = 10%)
    public decimal? RankingPrizeAccrued { get; set; } // snapshot já somado ao ranking (anti-dobra)
    public DateTimeOffset? RankingAccruedAt { get; set; }

    public bool IsActive { get; set; } = true;
    public Guid? CreatedBy { get; set; }

    // Navigation
    public HomeGame HomeGame { get; set; } = null!;
    public Ranking? Ranking { get; set; }
    public BlindStructure? BlindStructure { get; set; }
    public ICollection<TournamentEntry> Entries { get; set; } = [];
    public ICollection<TournamentTable> Tables { get; set; } = [];
    public ICollection<TournamentPrize> Prizes { get; set; } = [];
    public ICollection<CostExtra> CostExtras { get; set; } = [];
    public ICollection<Elimination> Eliminations { get; set; } = [];
    public ICollection<TournamentDealer> Dealers { get; set; } = [];
    public ICollection<TournamentBlindLevel> BlindLevels { get; set; } = [];
}
