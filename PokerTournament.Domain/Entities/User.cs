namespace PokerTournament.Domain.Entities;

public class User : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string Email { get; set; } = string.Empty;
    /// <summary>Hash PBKDF2 da senha. Null = conta só-social (entra pelo Google; pode definir senha depois — SPEC-20261023-1210).</summary>
    public string? PasswordHash { get; set; }
    public string? Whatsapp { get; set; }
    public string? PhotoUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
    /// <summary>Quando a pessoa clicou no link "Confirme seu e-mail" (null = login bloqueado). Contas anteriores à confirmação: created_at.</summary>
    public DateTimeOffset? EmailConfirmedAt { get; set; }
    // Plano da conta (SPEC-20261023-1205). O que vale num home game é o plano do DONO (HomeGame.OwnerId).
    // Plano efetivo = Plan se PlanExpiresAt for nulo ou futuro; senão Mesa (calculado na leitura, sem job).
    public string Plan { get; set; } = nameof(Enums.Plan.Mesa);
    public DateTimeOffset? PlanExpiresAt { get; set; }
    public string PlanSource { get; set; } = nameof(Enums.PlanSource.Manual);
    public string? PlanNote { get; set; }
    public DateTimeOffset? PlanUpdatedAt { get; set; }
    public Guid? PlanUpdatedBy { get; set; }
    // Boas-vindas (SPEC-20261023-1212): null = a conta ainda não passou pelas boas-vindas (o site leva a /boas-vindas).
    // Contas anteriores às boas-vindas (inclusive as fundadoras) foram marcadas pela migration.
    public DateTimeOffset? OnboardedAt { get; set; }
    /// <summary>Plano pago pedido nas boas-vindas (Clube|Liga). Só registra a intenção: o plano muda pelo Admin.</summary>
    public string? RequestedPlan { get; set; }
    public DateTimeOffset? RequestedPlanAt { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
    // Notificações (SPEC-20261108-1635): canal e categorias. Categoria desligada = a notificação nem é criada; e-mail
    // desligado = o sino continua, só o resumo por e-mail para. LastDigestAt limita o resumo a um por hora.
    public bool NotifyByEmail { get; set; } = true;
    public bool NotifyTournament { get; set; } = true;
    public bool NotifyRequests { get; set; } = true;
    public bool NotifySocial { get; set; } = true;
    public DateTimeOffset? LastDigestAt { get; set; }

    // Navigation
    public ICollection<HomeGame> OwnedHomeGames { get; set; } = [];
    public ICollection<HomeGameMember> HomeGameMemberships { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}
