using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PokerTournament.Api.Authorization;
using PokerTournament.Application.Common;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Hubs;

[Authorize]
// Dealer por QR conecta; JoinTournament restringe ao torneio do token
[AllowTokenKind(TokenKinds.Dealer, TournamentRouteKey = null)]
public class TournamentHub : Hub
{
    private readonly ILogger<TournamentHub> _logger;
    private readonly DealerAccessService _dealerAccess;
    private readonly DealerConnectionRegistry _dealerConnections;

    public TournamentHub(
        ILogger<TournamentHub> logger, DealerAccessService dealerAccess, DealerConnectionRegistry dealerConnections)
    {
        _logger = logger;
        _dealerAccess = dealerAccess;
        _dealerConnections = dealerConnections;
    }

    // Acesso do dealer (pedido revogado, QR desativado, prazo vencido, torneio terminado) é
    // revalidado em TODA chamada pelo DealerHubFilter, antes de chegar aqui
    public async Task JoinTournament(Guid tournamentId)
    {
        var kind = Context.User?.FindFirst(TokenKinds.ClaimType)?.Value;
        if (!string.IsNullOrEmpty(kind)
            && !string.Equals(Context.User?.FindFirst(TokenKinds.TournamentClaim)?.Value,
                tournamentId.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new HubException("Sem acesso a este torneio.");

        var groupName = $"tournament_{tournamentId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Conexão {ConnectionId} entrou no grupo {Group}", Context.ConnectionId, groupName);
    }

    public async Task LeaveTournament(Guid tournamentId)
    {
        var groupName = $"tournament_{tournamentId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Conexão {ConnectionId} saiu do grupo {Group}", Context.ConnectionId, groupName);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Cliente conectado: {ConnectionId}", Context.ConnectionId);

        // Dealer por QR: registra a conexão (sub = id do pedido aprovado) para que desconectar
        // o dealer ou desativar o QR a derrube. Registra ANTES de conferir: uma revogação
        // concorrente ou acha a conexão no registro, ou já marcou o pedido inativo no cache
        // e a conferência abaixo derruba (revogado entre o negotiate e a conexão).
        if (DealerConnectionRegistry.TryGetDealer(Context.User, out var requestId, out var tournamentId))
        {
            _dealerConnections.Add(Context, requestId, tournamentId);
            if (requestId == Guid.Empty || tournamentId == Guid.Empty
                || !await _dealerAccess.IsActiveAsync(requestId, Context.ConnectionAborted))
            {
                await _dealerConnections.DisconnectAsync(Context, requestId, tournamentId);
                return;
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _dealerConnections.Remove(Context.ConnectionId);
        _logger.LogInformation("Cliente desconectado: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
