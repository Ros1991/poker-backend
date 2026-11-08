namespace PokerTournament.Application.Interfaces;

/// <summary>
/// Costura única entre o perfil público / "jogou com você" (SPEC-20261108-1636) e a amizade (SPEC-20261108-1637).
/// Aqui nasce com a implementação nula (<see cref="Services.NoFriendshipLookup"/>: ninguém é amigo, ninguém está
/// bloqueado); a SPEC de amigos só troca o registro no <c>Program.cs</c>. Quem lê a regra: <c>PlayerProfileService</c>
/// (visibilidade <c>Friends</c> e o 404 do bloqueio) e <c>PlayedWithService</c> (bloqueado não aparece).
/// </summary>
public interface IFriendshipLookup
{
    /// <summary>As duas contas são amigas (amizade aceita).</summary>
    Task<bool> AreFriendsAsync(Guid userA, Guid userB, CancellationToken ct = default);

    /// <summary>Uma das duas bloqueou a outra (em qualquer sentido): nenhuma vê a outra.</summary>
    Task<bool> IsBlockedAsync(Guid userA, Guid userB, CancellationToken ct = default);
}
