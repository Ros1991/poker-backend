namespace PokerTournament.Application.DTOs.Responses;

// Convite do torneio e pedidos de vínculo "sou eu" (SPEC-20261108-1632).

/// <summary>Convite ativo do torneio (quem acessa o torneio): o código vai no QR/link <c>/entrar-no-torneio/{code}</c>.</summary>
public class TournamentInviteResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Pedidos "sou eu" aguardando o organizador neste torneio.</summary>
    public int PendingCount { get; set; }
}

/// <summary>Um inscrito sem conta, como aparece para quem lê o convite: só o nome de exibição e a foto do cadastro.</summary>
public class InvitePlayerResponse
{
    public Guid PersonId { get; set; }
    /// <summary>Apelido, senão o nome completo.</summary>
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
}

/// <summary><c>GET invite/{code}</c> (anônimo): o torneio e os inscritos ainda sem conta — nada de e-mail/WhatsApp.</summary>
public class InviteInfoResponse
{
    public Guid TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public string Status { get; set; } = string.Empty;
    /// <summary>Nome do clube (ou o nome do organizador no principal de dono Mesa).</summary>
    public string ClubName { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public List<InvitePlayerResponse> Players { get; set; } = [];
}

/// <summary>
/// <c>GET invite/{code}/my-request</c> (logado): o último pedido desta conta neste torneio (qualquer estado) e, se a
/// conta já é um jogador inscrito, quem ela é.
/// </summary>
public class MyLinkRequestResponse
{
    public Guid TournamentId { get; set; }
    /// <summary>A conta já tem um jogador vinculado inscrito neste torneio (nome de exibição); null = ainda não.</summary>
    public string? LinkedPlayerName { get; set; }
    /// <summary>Último pedido da conta neste torneio, ou null.</summary>
    public PlayerLinkRequestResponse? Request { get; set; }
}

/// <summary>Pedido de vínculo (lista do organizador, Início e a resposta do claim).</summary>
public class PlayerLinkRequestResponse
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public string Status { get; set; } = string.Empty;   // PlayerLinkRequestStatus
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    /// <summary>"Não estou na lista": sem jogador escolhido.</summary>
    public bool NotInList { get; set; }
    public string? Message { get; set; }
    public bool EmailCopied { get; set; }

    // A conta que pediu
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? UserNickname { get; set; }
    /// <summary>Só para quem gerencia (lista do organizador e Início): para cadastrar o jogador com este e-mail.</summary>
    public string? UserEmail { get; set; }
    public string? UserPhotoUrl { get; set; }

    // O jogador escolhido (null em "não estou na lista")
    public Guid? PersonId { get; set; }
    public string? PersonName { get; set; }
    public string? PersonPhotoUrl { get; set; }
}

/// <summary>Pedido pendente no Início (SPEC-20261108-1632): o pedido + o torneio e o home game, para agir dali mesmo.</summary>
public class InicioLinkRequest : PlayerLinkRequestResponse
{
    public string TournamentName { get; set; } = string.Empty;
    public Guid HomeGameId { get; set; }
    /// <summary>Nome do home game para a interface (null no principal de dono Mesa).</summary>
    public string? HomeGameName { get; set; }
}

/// <summary>Jogador vinculado à conta, na ficha da Administração (<c>GET admin/users/{id}</c>): soltar pelo <c>DELETE Persons/{id}/user-link</c>.</summary>
public class AdminUserLinkedPlayer
{
    public Guid PersonId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public string? PhotoUrl { get; set; }
    public Guid HomeGameId { get; set; }
    public string HomeGameName { get; set; } = string.Empty;
}
