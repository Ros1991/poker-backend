namespace PokerTournament.Application.DTOs.Responses;

// Amigos, pedidos e bloqueios (SPEC-20261108-1637). MESMA REGRA do perfil público: nada de nome completo, e-mail,
// WhatsApp, documento ou dinheiro de terceiros — o nome é o apelido ou só o primeiro nome (PlayerProfileService.DisplayNameOf).

/// <summary>Estado da relação entre quem vê e uma conta (perfil público e cartões do "jogou com você").</summary>
public static class FriendshipState
{
    /// <summary>A própria conta.</summary>
    public const string Self = "Self";
    /// <summary>Nada entre os dois (ou recusa já passada).</summary>
    public const string None = "None";
    /// <summary>Quem vê pediu e aguarda.</summary>
    public const string RequestSent = "RequestSent";
    /// <summary>A outra conta pediu a quem vê.</summary>
    public const string RequestReceived = "RequestReceived";
    /// <summary>Amigos.</summary>
    public const string Friends = "Friends";
}

/// <summary>A relação de quem vê com o perfil (<c>GET players/{handle}</c>). Bloqueio não chega aqui: o perfil é 404.</summary>
public class ProfileFriendshipResponse
{
    /// <summary><see cref="FriendshipState"/>.</summary>
    public string Status { get; set; } = FriendshipState.None;
    /// <summary>Id do pedido pendente (para aceitar, recusar ou cancelar); null fora de RequestSent/RequestReceived.</summary>
    public Guid? FriendshipId { get; set; }
    /// <summary>Quem vê pode pedir amizade agora (status None e a privacidade de quem recebe permite).</summary>
    public bool CanRequest { get; set; }
    /// <summary>Por que não pode (privacidade de quem recebe); null quando pode ou quando o status não é None.</summary>
    public string? CannotRequestReason { get; set; }
}

/// <summary>Uma conta vista por terceiros nas listas de amigos, pedidos, bloqueios e amigos em comum.</summary>
public class FriendUserResponse
{
    public Guid Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    /// <summary>Apelido; sem apelido, o primeiro nome.</summary>
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
}

/// <summary>Um amigo em <c>GET me/friends</c>.</summary>
public class FriendResponse : FriendUserResponse
{
    public DateTimeOffset FriendsSince { get; set; }
    /// <summary>O amigo está num torneio em andamento que quem vê pode acompanhar; null caso contrário.</summary>
    public FriendPlayingNowResponse? PlayingNow { get; set; }
}

/// <summary>"Jogando agora": o torneio em andamento do amigo e o caminho que quem vê pode abrir.</summary>
public class FriendPlayingNowResponse
{
    public Guid TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    /// <summary>Nome do clube (ou "Torneios de &lt;organizador&gt;" no principal de dono Mesa).</summary>
    public string HomeGameLabel { get; set; } = string.Empty;
    /// <summary>Caminho relativo do site: <c>/meus-torneios/{id}</c> (quem vê está inscrito) ou <c>/tournaments/{id}</c> (dono/membro do clube).</summary>
    public string Link { get; set; } = string.Empty;
}

/// <summary>Um pedido pendente (<c>GET me/friend-requests</c>): a outra conta e quando pediu.</summary>
public class FriendRequestResponse
{
    /// <summary>Id do registro (aceitar/recusar/cancelar).</summary>
    public Guid Id { get; set; }
    public FriendUserResponse User { get; set; } = new();
    public DateTimeOffset RequestedAt { get; set; }
}

/// <summary><c>GET me/friend-requests</c>: recebidos (para aceitar/recusar) e enviados (para cancelar), só pendentes.</summary>
public class FriendRequestsResponse
{
    public List<FriendRequestResponse> Received { get; set; } = [];
    public List<FriendRequestResponse> Sent { get; set; } = [];
}

/// <summary>Uma conta que eu bloqueei (<c>GET me/blocks</c>).</summary>
public class BlockedUserResponse : FriendUserResponse
{
    public DateTimeOffset BlockedAt { get; set; }
}

/// <summary>Resposta de pedir/aceitar/bloquear: a relação resultante (o perfil recarrega com ela).</summary>
public class FriendshipActionResponse
{
    public ProfileFriendshipResponse Friendship { get; set; } = new();
    /// <summary>Verdadeiro quando um pedido cruzado virou amizade na hora.</summary>
    public bool BecameFriends { get; set; }
}

/// <summary>Administração (<c>GET admin/users/{id}/blocks</c>): bloqueios feitos pela conta e sofridos por ela.</summary>
public class AdminUserBlocksResponse
{
    public List<AdminBlockEntryResponse> Made { get; set; } = [];
    public List<AdminBlockEntryResponse> Received { get; set; } = [];
}

/// <summary>Um bloqueio na ficha do Admin: a OUTRA conta (nome completo: é a Administração) e quando.</summary>
public class AdminBlockEntryResponse
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset BlockedAt { get; set; }
}
