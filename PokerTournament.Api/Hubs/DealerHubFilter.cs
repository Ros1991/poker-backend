using Microsoft.AspNetCore.SignalR;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Hubs;

/// <summary>
/// Reforço da revogação no <see cref="TournamentHub"/>: toda chamada de método feita com
/// token de dealer revalida o pedido (IsActiveAsync, cache de 30 s que a revogação marca
/// como inativo na hora). Acesso que não vale mais — desconectado, QR desativado, prazo
/// vencido, torneio terminado ou excluído — tem a chamada recusada e a conexão derrubada,
/// mesmo que o controller não a tenha encontrado no <see cref="DealerConnectionRegistry"/>.
/// Vale para qualquer método do hub, inclusive os que vierem a ser criados.
/// </summary>
public sealed class DealerHubFilter : IHubFilter
{
    private readonly DealerConnectionRegistry _connections;

    public DealerHubFilter(DealerConnectionRegistry connections)
    {
        _connections = connections;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var context = invocationContext.Context;
        if (DealerConnectionRegistry.TryGetDealer(context.User, out var requestId, out var tournamentId))
        {
            var access = invocationContext.ServiceProvider.GetRequiredService<DealerAccessService>();
            if (requestId == Guid.Empty || !await access.IsActiveAsync(requestId, context.ConnectionAborted))
            {
                await _connections.DisconnectAsync(context, requestId, tournamentId);
                throw new HubException("Acesso de dealer revogado ou expirado.");
            }
        }
        return await next(invocationContext);
    }
}
