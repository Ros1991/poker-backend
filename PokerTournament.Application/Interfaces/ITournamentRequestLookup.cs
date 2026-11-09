namespace PokerTournament.Application.Interfaces;

/// <summary>Um pedido de vaga em aberto: o torneio, o organizador e quem pediu (SPEC-20261108-1639).</summary>
public record TournamentRequestInfo(Guid TournamentId, Guid OrganizerUserId, Guid RequesterUserId);

/// <summary>
/// Costura entre as mensagens (SPEC-20261108-1638) e o pedido de vaga em torneio público (SPEC-20261108-1639). A conversa
/// <c>TournamentRequest</c> (organizador e interessado, sem amizade) só pode existir enquanto o pedido estiver vivo. Nasce
/// com a implementação nula <see cref="NullTournamentRequestLookup"/> registrada no <c>Program.cs</c>; a SPEC-20261108-1639
/// troca pela implementação real (a tabela <c>tournament_join_requests</c> é dela). O <c>ChatService</c> já usa os dois
/// métodos: <see cref="HasOpenRequestAsync"/> para a regra e <see cref="FindOpenRequestAsync"/> para o
/// <c>POST conversations {{ tournamentRequestId }}</c>.
/// </summary>
public interface ITournamentRequestLookup
{
    /// <summary>Há pedido de vaga em aberto de <paramref name="requesterUserId"/> no torneio, cujo organizador é <paramref name="organizerUserId"/>.</summary>
    Task<bool> HasOpenRequestAsync(Guid tournamentId, Guid organizerUserId, Guid requesterUserId, CancellationToken ct = default);

    /// <summary>
    /// O pedido de vaga em aberto pelo id, desde que <paramref name="callerUserId"/> seja o organizador ou quem pediu.
    /// Null = não existe, não está aberto ou não é de quem chama (404).
    /// </summary>
    Task<TournamentRequestInfo?> FindOpenRequestAsync(Guid requestId, Guid callerUserId, CancellationToken ct = default);
}

/// <summary>Sem pedidos de vaga (até a SPEC-20261108-1639): nenhuma conversa de torneio pode ser aberta.</summary>
public sealed class NullTournamentRequestLookup : ITournamentRequestLookup
{
    public Task<bool> HasOpenRequestAsync(Guid tournamentId, Guid organizerUserId, Guid requesterUserId, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<TournamentRequestInfo?> FindOpenRequestAsync(Guid requestId, Guid callerUserId, CancellationToken ct = default) =>
        Task.FromResult<TournamentRequestInfo?>(null);
}
