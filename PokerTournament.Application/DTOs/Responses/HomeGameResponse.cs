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
    /// <summary>
    /// Home game principal da conta do dono (SPEC-20261029-1723): nunca excluível; no Mesa e no Clube é o único e a
    /// interface não fala em "home game" (a conta é o clube).
    /// </summary>
    public bool IsPrimary { get; set; }
    /// <summary>
    /// O principal ainda tem o nome padrão "Clube de &lt;nome do dono&gt;" (dica "Dê um nome ao seu clube" em
    /// Configurações). Sempre false nos outros home games.
    /// </summary>
    public bool IsNameDefault { get; set; }
    /// <summary>
    /// Nome que a interface mostra: null no principal de dono Mesa (o frontend trata null como "sem home game": no Mesa
    /// não existe nome de clube); senão <see cref="Name"/> (o "nome do clube" no Clube/Liga). Preenchido pelo controller
    /// depois do plano (HomeGamePrimary.ApplyAsync).
    /// </summary>
    public string? DisplayName { get; set; }
    /// <summary>
    /// Quem está logado gerencia este home game: dono, membro administrador ou Admin do sistema (a regra do
    /// <c>HomeGameAccess.CanManageAsync</c>). O menu usa para mostrar Membros e Configurações (SPEC-20261029-1724).
    /// Preenchido pelo controller.
    /// </summary>
    public bool CanManage { get; set; }
    public int MemberCount { get; set; }
    public int TournamentCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Plano EFETIVO do dono (SPEC-20261023-1205) e os recursos que ele libera neste home game:
    // é por eles que o frontend decide cadeado (e não pelo plano de quem está logado).
    // Preenchidos pelo controller via PlanService depois do Map (não passam pelo MappingProfile).
    public string OwnerPlan { get; set; } = "Mesa";
    public List<string> PlanFeatures { get; set; } = [];
}
