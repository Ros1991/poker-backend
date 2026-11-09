namespace PokerTournament.Application.DTOs.Responses;

// Pedido de vaga, reserva e lista de espera em torneio público (SPEC-20261108-1639).

/// <summary>
/// Um pedido de vaga, como o organizador (painel, Início, terminal) e o interessado (página pública, Próximos) o veem.
/// Da conta que pediu só o nome público (apelido, senão o nome), o handle e a foto — nada de e-mail/WhatsApp; do
/// jogador criado na aprovação, o nome de exibição e a foto exibida.
/// </summary>
public class JoinRequestResponse
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    /// <summary>Pending | Approved | Waitlisted | Declined | Cancelled.</summary>
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    /// <summary>Posição na lista de espera (1 = a próxima); null fora dela.</summary>
    public int? WaitlistPosition { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    // A conta que pediu
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserHandle { get; set; } = string.Empty;
    public string? UserPhotoUrl { get; set; }

    // O jogador do clube (depois da aprovação)
    public Guid? PersonId { get; set; }
    public string? PersonName { get; set; }
    public string? PersonPhotoUrl { get; set; }
    /// <summary>A reserva já virou inscrição (saiu da fila "Reservas").</summary>
    public bool HasEntry { get; set; }
}

/// <summary><c>GET tournaments/{id}/join-requests</c> (quem gerencia): tudo do torneio de uma vez.</summary>
public class TournamentJoinOverviewResponse
{
    /// <summary>Aguardando decisão, o mais antigo primeiro.</summary>
    public List<JoinRequestResponse> Pending { get; set; } = [];
    /// <summary>Reservas aprovadas ainda sem inscrição (a fila "Reservas"), pela aprovação.</summary>
    public List<JoinRequestResponse> Reservations { get; set; } = [];
    /// <summary>Lista de espera, pela posição.</summary>
    public List<JoinRequestResponse> Waitlist { get; set; } = [];
    public int? MaxPlayers { get; set; }
    /// <summary>Inscrições + reservas sem inscrição.</summary>
    public int SpotsTaken { get; set; }
    /// <summary>Vagas que sobram (null sem limite).</summary>
    public int? SpotsLeft { get; set; }
}

/// <summary>Pedido pendente no Início (SPEC-20261108-1639): o pedido + o torneio e o home game, para decidir dali mesmo.</summary>
public class InicioJoinRequest : JoinRequestResponse
{
    public string TournamentName { get; set; } = string.Empty;
    public Guid HomeGameId { get; set; }
    /// <summary>Nome do home game para a interface (null no principal de dono Mesa).</summary>
    public string? HomeGameName { get; set; }
}

/// <summary>
/// <c>GET me/join-requests?tournamentId=</c>: o estado do botão "Quero participar" para a conta logada — o último pedido
/// dela no torneio (qualquer estado), se já está inscrita (jogador vinculado) e se gerencia o clube (não pede vaga).
/// </summary>
public class MyJoinStateResponse
{
    public Guid TournamentId { get; set; }
    public JoinRequestResponse? Request { get; set; }
    /// <summary>A conta já tem inscrição vinculada neste torneio.</summary>
    public bool Registered { get; set; }
    /// <summary>A conta gerencia o home game do torneio (dona, membro administrador ou Admin).</summary>
    public bool Manages { get; set; }
    /// <summary>Pode pedir agora: torneio público e não encerrado, sem pedido aberto, não recusado, não inscrita, não gerencia.</summary>
    public bool CanRequest { get; set; }
}

/// <summary>
/// Um torneio futuro da conta em "Meus torneios › Próximos" (SPEC-20261108-1639): pedido aberto (aguardando, reservado
/// ou na espera) ou inscrição vinculada ainda não começada. Endereço completo e "Como chegar" SÓ com a reserva aprovada
/// ou a inscrição feita (D6); nos demais, só bairro e cidade.
/// </summary>
public class MyUpcomingTournamentResponse
{
    public Guid TournamentId { get; set; }
    /// <summary>Slug da página pública (null quando o torneio não é público).</summary>
    public string? PublicSlug { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid HomeGameId { get; set; }
    public string HomeGameLabel { get; set; } = string.Empty;
    public decimal BuyInAmount { get; set; }
    /// <summary>A conta tem inscrição vinculada (ou a reserva já virou inscrição).</summary>
    public bool Registered { get; set; }
    public Guid? RequestId { get; set; }
    /// <summary>Pending | Approved | Waitlisted (null sem pedido: inscrição direta).</summary>
    public string? RequestStatus { get; set; }
    public int? WaitlistPosition { get; set; }
    /// <summary>"Bairro, Cidade - UF" (nunca a rua para quem não foi aprovado); null sem endereço.</summary>
    public string? LocationSummary { get; set; }
    /// <summary>Endereço completo — só aprovado/inscrito; null nos demais.</summary>
    public string? FullAddress { get; set; }
    /// <summary>"Como chegar": link de mapa com as coordenadas (ou o endereço) — só aprovado/inscrito.</summary>
    public string? MapUrl { get; set; }
}
