namespace PokerTournament.Domain.Enums;

/// <summary>
/// Estado do registro do par em <c>friendships</c> (SPEC-20261108-1637). Gravado como string via <c>nameof</c>.
/// Pending = pedido aguardando quem recebeu; Accepted = amigos (sem direção); Declined = recusado (quem pediu não é
/// avisado e só pode pedir de novo depois de 30 dias); Cancelled = quem pediu cancelou (revisão da onda 2: a linha fica
/// com <c>RespondedAt</c> para pedir de novo em menos de 24 h NÃO gerar nova notificação); Blocked = uma das contas
/// bloqueou a outra (<c>BlockedById</c>). As listas e contagens só olham Pending/Accepted/Blocked: Declined e Cancelled
/// são invisíveis e viram o pedido novo quando alguém pede de novo.
/// </summary>
public enum FriendshipStatus
{
    Pending,
    Accepted,
    Declined,
    Cancelled,
    Blocked
}
