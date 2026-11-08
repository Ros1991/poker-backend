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

    // Perfil público e privacidade (SPEC-20261108-1636). Handle = apelido único da URL (/jogador/{handle}), minúsculas,
    // 3–30 caracteres [a-z0-9-], gerado do apelido/nome no cadastro (HandleGenerator) e editável no Meu perfil; a
    // migration PerfilDoJogador deu um a cada conta existente. Cidade/UF e bio são do perfil (nada de geocodificar aqui:
    // a SPEC-20261108-1639 geocodifica cidade/UF na busca). Os controles guardam nameof dos enums; padrões conservadores
    // (D12): perfil para todos, pedido de amizade de quem jogou comigo, mensagem só de amigos, aparecer em "jogou com você".
    public string Handle { get; set; } = string.Empty;
    public string? City { get; set; }
    /// <summary>UF com 2 letras maiúsculas (a mesma lista do endereço do home game).</summary>
    public string? State { get; set; }
    /// <summary>Até 200 caracteres.</summary>
    public string? Bio { get; set; }
    public string ProfileVisibility { get; set; } = nameof(Enums.ProfileVisibility.Everyone);
    public string FriendRequestsFrom { get; set; } = nameof(Enums.FriendRequestsFrom.PlayedWith);
    public string MessagesFrom { get; set; } = nameof(Enums.MessagesFrom.Friends);
    public bool ShowInPlayedWith { get; set; } = true;
    /// <summary>
    /// Opt-in (revisão independente da onda 1): o WhatsApp da conta aparece como "Chamar o organizador" na área do
    /// jogador dos torneios que ela criou (ou dos clubes de que é dona). Padrão desligado: é o telefone pessoal.
    /// </summary>
    public bool ShowWhatsappToPlayers { get; set; }

    // Navigation
    public ICollection<HomeGame> OwnedHomeGames { get; set; } = [];
    public ICollection<HomeGameMember> HomeGameMemberships { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}
