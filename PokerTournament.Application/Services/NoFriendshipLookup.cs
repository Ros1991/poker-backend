using PokerTournament.Application.Interfaces;

namespace PokerTournament.Application.Services;

/// <summary>
/// Implementação nula da costura de amizade (SPEC-20261108-1636): sem a SPEC-20261108-1637 ninguém é amigo e ninguém
/// está bloqueado — o perfil <c>Friends</c> fica fechado para todos os terceiros e o "jogou com você" não filtra nada.
/// </summary>
public sealed class NoFriendshipLookup : IFriendshipLookup
{
    public Task<bool> AreFriendsAsync(Guid userA, Guid userB, CancellationToken ct = default) => Task.FromResult(false);

    public Task<bool> IsBlockedAsync(Guid userA, Guid userB, CancellationToken ct = default) => Task.FromResult(false);
}
