using PokerTournament.Application.Plans;

namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// GET api/v1/me/plan (SPEC-20261023-1205): plano da conta logada, limites e recursos do plano
/// efetivo, uso atual e o catálogo inteiro (tabela comparativa da página "Meu plano").
/// </summary>
public class MyPlanResponse
{
    public string Plan { get; set; } = PlanCatalog.Mesa;
    public string EffectivePlan { get; set; } = PlanCatalog.Mesa;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string Source { get; set; } = "Manual";
    public string? Note { get; set; }
    public PlanLimitsResponse Limits { get; set; } = new();
    public List<string> Features { get; set; } = [];
    public PlanUsageResponse Usage { get; set; } = new();
    public IReadOnlyList<PlanDefinition> Catalog { get; set; } = [];
}

public class PlanLimitsResponse
{
    public int MaxOwnedHomeGames { get; set; }
    public int? MaxMembersPerHomeGame { get; set; }   // null = ilimitado
    public int? TournamentsPer7Days { get; set; }     // null = ilimitado
}

public class PlanUsageResponse
{
    public PlanUsageCounter HomeGames { get; set; } = new();
    public PlanTournamentUsage TournamentsLast7Days { get; set; } = new();
    /// <summary>Mesmo valor de TournamentsLast7Days.NextAllowedAt (nome do critério de aceite).</summary>
    public DateTimeOffset? NextTournamentAllowedAt { get; set; }
    public List<PlanMemberUsage> MembersByHomeGame { get; set; } = [];
}

public class PlanUsageCounter
{
    public int Used { get; set; }
    public int? Limit { get; set; }
}

public class PlanTournamentUsage
{
    public int Used { get; set; }
    public int? Limit { get; set; }
    /// <summary>Quando o próximo torneio libera; null = já pode criar (ou sem limite).</summary>
    public DateTimeOffset? NextAllowedAt { get; set; }
}

public class PlanMemberUsage
{
    public Guid HomeGameId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Used { get; set; }
    public int? Limit { get; set; }
}
