namespace PokerTournament.Application.DTOs.Responses;

// Meu perfil (SPEC-20261023-1211): respostas de me/*.

/// <summary><c>GET/PUT me</c>, <c>POST/DELETE me/photo</c>: os dados da conta logada.</summary>
public class ProfileResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    /// <summary>Nome da conta (o mesmo <c>name</c> do usuário da sessão).</summary>
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    /// <summary>55 + DDD + número, só dígitos (ou o valor antigo como estava, até a pessoa salvar o perfil).</summary>
    public string? Whatsapp { get; set; }
    public string? PhotoUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    /// <summary>Falso = conta que só entra pelo Google (pode definir senha pelo link do e-mail).</summary>
    public bool HasPassword { get; set; }
    /// <summary>E-mail de retransmissão da Apple ("…@privaterelay.appleid.com"): o perfil mostra o botão Copiar.</summary>
    public bool EmailIsPrivateRelay { get; set; }
    public DateTimeOffset? EmailConfirmedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    /// <summary>Home games dos quais a conta é dona (com algum, excluir a conta fica bloqueado).</summary>
    public int OwnedHomeGames { get; set; }
    /// <summary>Plano efetivo (atalho para "Meu plano").</summary>
    public string EffectivePlan { get; set; } = "Mesa";
}

/// <summary><c>GET/POST/DELETE me/logins</c>: formas de entrar na conta.</summary>
public class ExternalLoginsResponse
{
    public bool HasPassword { get; set; }
    /// <summary>Um item por provedor suportado (Google; Apple só quando estiver ligada ou já conectada).</summary>
    public List<ExternalLoginItem> Providers { get; set; } = [];
}

public class ExternalLoginItem
{
    /// <summary>"google" | "apple" (o mesmo segmento de <c>me/logins/{provider}</c>).</summary>
    public string Provider { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Provedor ligado no servidor (dá para conectar agora).</summary>
    public bool Enabled { get; set; }
    /// <summary>Client ID público (para o botão do provedor); null com o provedor desligado.</summary>
    public string? ClientId { get; set; }
    public bool Connected { get; set; }
    /// <summary>Contas do provedor ligadas a esta conta (normalmente uma; o mais recente primeiro).</summary>
    public List<ExternalLoginAccount> Accounts { get; set; } = [];
    /// <summary>
    /// Falso = é a única forma de entrar (conta sem senha e sem login de outro provedor): desconectar é bloqueado.
    /// Sempre falso quando não está conectado.
    /// </summary>
    public bool CanDisconnect { get; set; }
}

public class ExternalLoginAccount
{
    /// <summary>E-mail que o provedor informou na última entrada.</summary>
    public string? Email { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary><c>GET me/sessions</c>: um aparelho conectado (sessão ativa em auth_sessions).</summary>
public class SessionResponse
{
    public Guid Id { get; set; }
    /// <summary>"Chrome no Windows", "Safari no iPhone"…</summary>
    public string Label { get; set; } = string.Empty;
    public string Browser { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    /// <summary>"desktop" | "mobile" | "tablet" | "unknown" (o ícone da lista).</summary>
    public string DeviceType { get; set; } = "unknown";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    /// <summary>É a sessão deste aparelho (a do token da requisição).</summary>
    public bool Current { get; set; }
}

/// <summary><c>POST me/password</c>.</summary>
public class ChangePasswordResponse
{
    public string Message { get; set; } = string.Empty;
    /// <summary>Quantos outros aparelhos saíram da conta.</summary>
    public int OtherSessionsRevoked { get; set; }
}

/// <summary><c>POST me/sessions/revoke-others</c>.</summary>
public class RevokeSessionsResponse
{
    public int Revoked { get; set; }
}

/// <summary><c>POST me/password/setup-link</c> (202).</summary>
public class PasswordSetupLinkResponse
{
    public string Message { get; set; } = string.Empty;
    /// <summary>Para onde foi o link (mascarado: "ro***@gmail.com").</summary>
    public string Email { get; set; } = string.Empty;
}
