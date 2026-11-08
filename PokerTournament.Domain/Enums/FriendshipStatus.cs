namespace PokerTournament.Domain.Enums;

/// <summary>
/// Estado do registro do par em <c>friendships</c> (SPEC-20261108-1637). Gravado como string via <c>nameof</c>.
/// Pending = pedido aguardando quem recebeu; Accepted = amigos (sem direção); Declined = recusado (quem pediu não é
/// avisado e só pode pedir de novo depois de 30 dias); Blocked = uma das contas bloqueou a outra (<c>BlockedById</c>).
/// </summary>
public enum FriendshipStatus
{
    Pending,
    Accepted,
    Declined,
    Blocked
}
