namespace PokerTournament.Domain.Enums;

/// <summary>
/// De quem a conta aceita pedido de amizade (SPEC-20261108-1636; a regra vale a partir da SPEC-20261108-1637).
/// Padrão conservador (D12): PlayedWith — só quem já jogou um torneio com a conta.
/// </summary>
public enum FriendRequestsFrom
{
    Everyone,
    PlayedWith,
    Nobody
}
