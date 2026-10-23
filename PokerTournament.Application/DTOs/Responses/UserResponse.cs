namespace PokerTournament.Application.DTOs.Responses;

public class UserResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public bool IsActive { get; set; }

    // Plano da conta (SPEC-20261023-1205): o gravado, o efetivo (Mesa depois do vencimento),
    // vencimento, origem (Manual | Cortesia | Trial | Assinatura) e nota (ex.: "fundador")
    public string Plan { get; set; } = "Mesa";
    public string EffectivePlan { get; set; } = "Mesa";
    public DateTimeOffset? PlanExpiresAt { get; set; }
    public string PlanSource { get; set; } = "Manual";
    public string? PlanNote { get; set; }
}
