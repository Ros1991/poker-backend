namespace PokerTournament.Domain.Enums;

/// <summary>
/// Tipo da conversa (SPEC-20261108-1638), gravado como string via <c>nameof</c> em <c>Conversation.Type</c>.
/// Direct = 1 para 1 entre amigos (ou organizador liberado por <c>MessagesFrom = FriendsAndOrganizers</c>), uma por par;
/// TournamentRequest = organizador e interessado a partir do pedido de vaga de um torneio público (SPEC-20261108-1639),
/// uma por par e torneio, sem exigir amizade.
/// </summary>
public enum ConversationType
{
    Direct,
    TournamentRequest
}
