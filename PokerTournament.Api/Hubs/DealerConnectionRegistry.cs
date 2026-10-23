using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using PokerTournament.Application.Common;

namespace PokerTournament.Api.Hubs;

/// <summary>
/// Conexões abertas do hub feitas com token de dealer (QR), por pedido aprovado (sub do
/// token) e torneio. A conexão SignalR sobrevive à revogação — o token só é conferido ao
/// conectar —, então quem desconecta um dealer (ou desativa o QR) usa este registro para
/// tirar a conexão do grupo do torneio, avisar o aparelho ("DealerRevoked") e derrubá-la.
/// Preenchido no OnConnectedAsync do <see cref="TournamentHub"/> e limpo no
/// OnDisconnectedAsync. Em memória: vale para a instância da API (o deploy é instância única).
/// </summary>
public sealed class DealerConnectionRegistry
{
    /// <summary>Evento que o aparelho do dealer recebe antes de a conexão cair.</summary>
    public const string RevokedEvent = "DealerRevoked";
    public const string RevokedMessage = "Seu acesso de dealer foi encerrado pelo organizador.";
    public const string ExpiredMessage = "Seu acesso de dealer foi encerrado ou venceu.";

    // Dá tempo de o aviso sair pelo socket antes do Abort (que descarta o que estiver na fila)
    private static readonly TimeSpan AbortDelay = TimeSpan.FromMilliseconds(500);

    private sealed record Entry(Guid RequestId, Guid TournamentId, HubCallerContext Context);

    private readonly ConcurrentDictionary<string, Entry> _byConnection = new();
    private readonly IHubContext<TournamentHub> _hub;
    private readonly ILogger<DealerConnectionRegistry> _logger;

    public DealerConnectionRegistry(IHubContext<TournamentHub> hub, ILogger<DealerConnectionRegistry> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// Pedido (sub) e torneio de um token de dealer. False para token de usuário; para token
    /// de dealer malformado, true com Guid.Empty (quem chama derruba a conexão).
    /// </summary>
    public static bool TryGetDealer(ClaimsPrincipal? user, out Guid requestId, out Guid tournamentId)
    {
        requestId = Guid.Empty;
        tournamentId = Guid.Empty;
        if (user?.FindFirst(TokenKinds.ClaimType)?.Value != TokenKinds.Dealer)
            return false;
        var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        if (Guid.TryParse(sub, out var parsedRequest))
            requestId = parsedRequest;
        if (Guid.TryParse(user.FindFirst(TokenKinds.TournamentClaim)?.Value, out var parsedTournament))
            tournamentId = parsedTournament;
        return true;
    }

    public void Add(HubCallerContext context, Guid requestId, Guid tournamentId) =>
        _byConnection[context.ConnectionId] = new Entry(requestId, tournamentId, context);

    public void Remove(string connectionId) => _byConnection.TryRemove(connectionId, out _);

    /// <summary>Organizador desconectou o pedido: derruba as conexões daquele aparelho.</summary>
    public Task DisconnectRequestAsync(Guid requestId) =>
        DisconnectAllAsync(e => e.RequestId == requestId, RevokedMessage);

    /// <summary>QR desativado (DELETE dealer-access): derruba todos os dealers do torneio.</summary>
    public Task DisconnectTournamentAsync(Guid tournamentId) =>
        DisconnectAllAsync(e => e.TournamentId == tournamentId, RevokedMessage);

    /// <summary>Uma conexão cujo acesso não vale mais (revalidação no hub).</summary>
    public Task DisconnectAsync(HubCallerContext context, Guid requestId, Guid tournamentId) =>
        DisconnectEntryAsync(new Entry(requestId, tournamentId, context), ExpiredMessage);

    private async Task DisconnectAllAsync(Func<Entry, bool> match, string message)
    {
        foreach (var entry in _byConnection.Values.Where(match).ToList())
            await DisconnectEntryAsync(entry, message);
    }

    // Tira do grupo ANTES de avisar: nenhum evento do torneio chega depois do aviso. A
    // queda vem logo em seguida, sem segurar a resposta do organizador.
    private async Task DisconnectEntryAsync(Entry entry, string message)
    {
        var connectionId = entry.Context.ConnectionId;
        _byConnection.TryRemove(connectionId, out _);
        try
        {
            await _hub.Groups.RemoveFromGroupAsync(connectionId, $"tournament_{entry.TournamentId}");
            await _hub.Clients.Client(connectionId).SendAsync(RevokedEvent,
                new { requestId = entry.RequestId, message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao avisar a conexão {ConnectionId} do dealer revogado", connectionId);
        }
        _ = AbortLaterAsync(entry.Context);
        _logger.LogInformation("Conexão {ConnectionId} do dealer {RequestId} derrubada", connectionId, entry.RequestId);
    }

    private async Task AbortLaterAsync(HubCallerContext context)
    {
        try
        {
            await Task.Delay(AbortDelay);
            context.Abort();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao derrubar a conexão {ConnectionId} do dealer", context.ConnectionId);
        }
    }
}
