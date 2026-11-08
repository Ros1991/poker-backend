namespace PokerTournament.Domain.Enums;

/// <summary>
/// De quem a conta aceita mensagem (SPEC-20261108-1636; a regra vale a partir da SPEC-20261108-1638).
/// Friends = só amigos (padrão, D12); FriendsAndOrganizers = também organizadores de torneios públicos em que a conta
/// pediu vaga (SPEC-20261108-1639).
/// </summary>
public enum MessagesFrom
{
    Friends,
    FriendsAndOrganizers
}
