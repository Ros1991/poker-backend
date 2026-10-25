using System.Text.Json;

namespace PokerTournament.Application.DTOs.Responses;

// Administração geral (SPEC-20261023-1206): respostas de api/v1/admin/users (só Admin do sistema).
// UserResponse e AuthResponse não mudam; UserLookupResponse (modal "Adicionar membro") fica em arquivo próprio.

/// <summary>Uma linha da listagem <c>GET admin/users</c> (paginada).</summary>
public class AdminUserListItemResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    /// <summary>Conta excluída pela própria pessoa (anonimizada, SPEC-20261023-1211): não volta a ser ativada.</summary>
    public bool IsDeleted { get; set; }
    /// <summary>Falso = conta só-social (entra pelo Google, sem senha).</summary>
    public bool HasPassword { get; set; }
    public DateTimeOffset? EmailConfirmedAt { get; set; }
    /// <summary>Verdadeiro = ainda não passou pelas boas-vindas (onboarded_at nulo).</summary>
    public bool NeedsOnboarding { get; set; }

    // Plano (SPEC-20261023-1205): o gravado, o efetivo (Mesa depois do vencimento), vencimento, origem e nota
    public string Plan { get; set; } = "Mesa";
    public string EffectivePlan { get; set; } = "Mesa";
    public DateTimeOffset? PlanExpiresAt { get; set; }
    public string PlanSource { get; set; } = "Manual";
    public string? PlanNote { get; set; }
    /// <summary>Plano pago pedido nas boas-vindas (SPEC-20261023-1212) e quando.</summary>
    public string? RequestedPlan { get; set; }
    public DateTimeOffset? RequestedPlanAt { get; set; }
    /// <summary>O pedido ainda não foi atendido (o plano efetivo está abaixo do pedido).</summary>
    public bool PlanRequestPending { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
    /// <summary>Último uso de uma sessão não revogada (o refresh desliza; quem nunca sai tem login antigo e uso recente).</summary>
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Home games dos quais a conta é dona.</summary>
    public int OwnedHomeGamesCount { get; set; }
}

/// <summary><c>GET admin/users/{id}</c>: a ficha da conta.</summary>
public class AdminUserDetailResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string Email { get; set; } = string.Empty;
    /// <summary>55 + DDD + número, só dígitos (ou o valor antigo como estava).</summary>
    public string? Whatsapp { get; set; }
    public string? PhotoUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // E-mail e formas de entrar
    public DateTimeOffset? EmailConfirmedAt { get; set; }
    public bool HasPassword { get; set; }
    public List<AdminUserExternalLogin> ExternalLogins { get; set; } = [];
    /// <summary>O servidor envia e-mail de verdade (Resend)? Falso = só "senha temporária" e "link" funcionam.</summary>
    public bool EmailDeliveryEnabled { get; set; }

    // Boas-vindas (SPEC-20261023-1212)
    public DateTimeOffset? OnboardedAt { get; set; }
    public bool NeedsOnboarding { get; set; }
    public string? RequestedPlan { get; set; }
    public DateTimeOffset? RequestedPlanAt { get; set; }
    public bool PlanRequestPending { get; set; }

    // Acesso
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    /// <summary>Sessões não revogadas e no prazo.</summary>
    public int ActiveSessions { get; set; }
    public List<AdminUserSession> Sessions { get; set; } = [];

    // Home games
    public List<AdminUserOwnedHomeGame> OwnedHomeGames { get; set; } = [];
    public List<AdminUserMembership> Memberships { get; set; } = [];

    // Plano
    public UserPlanResponse Plan { get; set; } = new();
    /// <summary>PlanChange (inclusive a cortesia "fundador" da migration) e PlanRequested, mais recentes primeiro (até 50).</summary>
    public List<UserPlanHistoryEntry> PlanHistory { get; set; } = [];
    /// <summary>Ações administrativas sobre a conta (editar, senha, status, e-mail), mais recentes primeiro (até 50).</summary>
    public List<UserAuditEntryResponse> AdminActions { get; set; } = [];
}

/// <summary>Plano da conta na ficha.</summary>
public class UserPlanResponse
{
    public string Plan { get; set; } = "Mesa";
    public string EffectivePlan { get; set; } = "Mesa";
    public DateTimeOffset? ExpiresAt { get; set; }
    public string Source { get; set; } = "Manual";
    public string? Note { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    /// <summary>Nome de quem trocou por último (null = migration ou conta que não existe mais).</summary>
    public string? UpdatedByName { get; set; }
}

/// <summary>
/// Uma entrada do histórico do plano, lida do audit_logs. <c>Action</c> = <c>PlanChange</c> (troca: de → para) ou
/// <c>PlanRequested</c> (pedido nas boas-vindas: <see cref="RequestedPlan"/>, plano da época em <see cref="FromPlan"/>).
/// </summary>
public class UserPlanHistoryEntry
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ActorId { get; set; }
    /// <summary>Quem fez ("migration" na cortesia dos fundadores).</summary>
    public string? ActorName { get; set; }
    public string? FromPlan { get; set; }
    public string? ToPlan { get; set; }
    public string? FromSource { get; set; }
    public string? ToSource { get; set; }
    public DateTimeOffset? FromExpiresAt { get; set; }
    public DateTimeOffset? ToExpiresAt { get; set; }
    public string? FromNote { get; set; }
    public string? ToNote { get; set; }
    public string? RequestedPlan { get; set; }
    public string? Via { get; set; }
}

/// <summary>Uma linha do audit_logs da conta (ações administrativas). Valores em JSON como foram gravados.</summary>
public class UserAuditEntryResponse
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorName { get; set; }
    public JsonElement? OldValues { get; set; }
    public JsonElement? NewValues { get; set; }
    public JsonElement? ChangedFields { get; set; }
}

public class AdminUserExternalLogin
{
    /// <summary>"Google" (nome de exibição do provedor).</summary>
    public string Provider { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

public class AdminUserSession
{
    public Guid Id { get; set; }
    /// <summary>"Chrome no Windows", "Safari no iPhone"…</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>"desktop" | "mobile" | "tablet" | "unknown".</summary>
    public string DeviceType { get; set; } = "unknown";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public class AdminUserOwnedHomeGame
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TournamentsCount { get; set; }
    public int MembersCount { get; set; }
}

public class AdminUserMembership
{
    public Guid HomeGameId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string? OwnerName { get; set; }
}

/// <summary>
/// <c>POST admin/users/{id}/password</c>. <c>Temporary</c>: só o modo. <c>Email</c>: <see cref="SentTo"/> mascarado.
/// <c>Link</c>: <see cref="ResetUrl"/> ({PUBLIC_APP_URL}/redefinir-senha/{token}), validade e o WhatsApp da conta
/// (só dígitos, 55 + DDD + número; null = sem número — o site só oferece "Copiar").
/// </summary>
public class AdminResetPasswordResponse
{
    public string Mode { get; set; } = string.Empty;
    public string? SentTo { get; set; }
    public string? ResetUrl { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Whatsapp { get; set; }
}

/// <summary><c>POST admin/users/{id}/confirmation-email</c>: para onde foi o link (mascarado).</summary>
public class AdminEmailSentResponse
{
    public string SentTo { get; set; } = string.Empty;
}
