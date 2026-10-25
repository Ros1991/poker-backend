namespace PokerTournament.Application.DTOs.Requests;

// Administração geral (SPEC-20261023-1206): corpos de api/v1/admin/users. Não existe criação de conta pela
// administração (D14): toda conta nasce pelo cadastro da própria pessoa.

/// <summary>
/// <c>PUT admin/users/{id}</c>: dados da conta. Campo null = não mexe. Nome e e-mail, quando vêm, não podem ser vazios;
/// apelido e WhatsApp vazios apagam. WhatsApp na regra do perfil (55 + DDD + número). Papel: Admin | TournamentDirector
/// | Dealer | Player (trocar o papel derruba as sessões). SEM senha: senha tem endpoint próprio
/// (<c>POST admin/users/{id}/password</c>).
/// </summary>
public class UpdateUserRequest
{
    public string? FullName { get; set; }
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    public string? Role { get; set; }
}

/// <summary>
/// <c>PUT admin/users/{id}/plan</c>: plano Mesa | Clube | Liga, vencimento (null = sem vencimento; se vier, no futuro),
/// origem Manual | Cortesia | Trial | Assinatura (vazio = Manual) e observação até 200 caracteres (ex.: "fundador").
/// Grava pelo mesmo <c>PlanService.ChangePlanAsync</c> da SPEC-20261023-1205 (AuditLog PlanChange).
/// </summary>
public class ChangeUserPlanRequest
{
    public string Plan { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Source { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// <c>POST admin/users/{id}/password</c>. Mode: <c>Temporary</c> (exige <see cref="Password"/>, mín. 6; derruba todas
/// as sessões), <c>Email</c> (e-mail de redefinição da SPEC-20261023-1203; 409 sem envio configurado) ou <c>Link</c>
/// (link de uso único válido por 24 h, para mandar por WhatsApp). A senha e o token nunca vão para o audit_logs.
/// </summary>
public class AdminResetPasswordRequest
{
    public string Mode { get; set; } = string.Empty;
    public string? Password { get; set; }
}

/// <summary><c>PATCH admin/users/{id}/status</c>: ativar ou desativar a conta (desativar derruba todas as sessões).</summary>
public class ChangeUserStatusRequest
{
    public bool IsActive { get; set; }
}
