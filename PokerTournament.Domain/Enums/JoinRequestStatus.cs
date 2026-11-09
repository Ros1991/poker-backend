namespace PokerTournament.Domain.Enums;

/// <summary>
/// Estado do pedido de vaga num torneio público (SPEC-20261108-1639), gravado como string via <c>nameof</c> em
/// <c>TournamentJoinRequest.Status</c>. "Aberto" = Pending, Approved ou Waitlisted (um por conta e torneio).
/// </summary>
public enum JoinRequestStatus
{
    /// <summary>Aguardando o organizador.</summary>
    Pending,
    /// <summary>Vaga reservada: o jogador existe no clube (vinculado à conta) e aparece na fila "Reservas" até ser inscrito.</summary>
    Approved,
    /// <summary>Vagas esgotadas: na lista de espera, na posição <c>WaitlistPosition</c>; sobe sozinho quando uma reserva sai.</summary>
    Waitlisted,
    /// <summary>Recusado pelo organizador: a conta não pede de novo neste torneio.</summary>
    Declined,
    /// <summary>Cancelado pelo interessado (ou reserva removida pelo organizador): a conta pode pedir de novo.</summary>
    Cancelled,
}
