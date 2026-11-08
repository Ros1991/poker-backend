namespace PokerTournament.Application.Interfaces;

/// <summary>
/// Costura única entre o perfil público / "jogou com você" (SPEC-20261108-1636) e a amizade (SPEC-20261108-1637).
/// Nasceu com uma implementação nula; desde a SPEC-20261108-1637 é o <see cref="Services.FriendshipService"/>
/// (registrado no <c>Program.cs</c>). Quem lê a regra: <c>PlayerProfileService</c> (visibilidade <c>Friends</c> e o 404
/// do bloqueio), <c>PlayedWithService</c> (bloqueado não aparece) e, na SPEC-20261108-1638, as mensagens
/// (<see cref="IsBlockedAsync"/> é o ÚNICO ponto a consultar: bloqueio encerra e esconde a conversa).
/// </summary>
public interface IFriendshipLookup
{
    /// <summary>As duas contas são amigas (amizade aceita).</summary>
    Task<bool> AreFriendsAsync(Guid userA, Guid userB, CancellationToken ct = default);

    /// <summary>Uma das duas bloqueou a outra (em qualquer sentido): nenhuma vê a outra.</summary>
    Task<bool> IsBlockedAsync(Guid userA, Guid userB, CancellationToken ct = default);
}
