namespace PokerTournament.Application.DTOs.Responses;

public class HomeGameResponse
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? LogoUrl { get; set; }
    public string? PixKey { get; set; }
    public string? PixBeneficiary { get; set; }
    public string Timezone { get; set; } = string.Empty;
    public decimal? DefaultBuyIn { get; set; }
    public decimal? DefaultRebuy { get; set; }
    public decimal? DefaultAddon { get; set; }
    public bool IsActive { get; set; }
    public int MemberCount { get; set; }
    public int TournamentCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Plano EFETIVO do dono (SPEC-20261023-1205) e os recursos que ele libera neste home game:
    // é por eles que o frontend decide cadeado (e não pelo plano de quem está logado).
    // Preenchidos pelo controller via PlanService depois do Map (não passam pelo MappingProfile).
    public string OwnerPlan { get; set; } = "Mesa";
    public List<string> PlanFeatures { get; set; } = [];
}
