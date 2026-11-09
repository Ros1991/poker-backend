namespace PokerTournament.Application.Interfaces;

/// <summary>
/// Costura única entre o perfil público / "jogou com você" (SPEC-20261108-1636) e a amizade (SPEC-20261108-1637).
/// Nasceu com uma implementação nula; desde a SPEC-20261108-1637 é o <see cref="Services.FriendshipService"/>
/// (registrado no <c>Program.cs</c>). Quem lê a regra: <c>PlayerProfileService</c> (visibilidade <c>Friends</c> e o 404
/// do bloqueio), <c>PlayedWithService</c> (bloqueado não aparece) e, desde a SPEC-20261108-1638, o <c>ChatService</c>
/// (<see cref="AreFriendsAsync"/> para abrir conversa; <see cref="IsBlockedAsync"/> e <see cref="BlockedUserIdsAsync"/>
/// são o ÚNICO ponto a consultar para o bloqueio: bloqueio encerra e esconde a conversa nos dois lados).
/// </summary>
public interface IFriendshipLookup
{
    /// <summary>As duas contas são amigas (amizade aceita).</summary>
    Task<bool> AreFriendsAsync(Guid userA, Guid userB, CancellationToken ct = default);

    /// <summary>Uma das duas bloqueou a outra (em qualquer sentido): nenhuma vê a outra.</summary>
    Task<bool> IsBlockedAsync(Guid userA, Guid userB, CancellationToken ct = default);

    /// <summary>
    /// Todas as contas com bloqueio em qualquer sentido com <paramref name="userId"/> (SPEC-20261108-1638): a lista e a
    /// contagem de conversas filtram numa consulta só, em vez de perguntar par a par.
    /// </summary>
    Task<HashSet<Guid>> BlockedUserIdsAsync(Guid userId, CancellationToken ct = default);
}
