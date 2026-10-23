using System.Security.Claims;
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

    public TournamentHub(ILogger<TournamentHub> logger, DealerAccessService dealerAccess)
    {
        _logger = logger;
        _dealerAccess = dealerAccess;
    }

    public async Task JoinTournament(Guid tournamentId)
    {
        var kind = Context.User?.FindFirst(TokenKinds.ClaimType)?.Value;
        if (!string.IsNullOrEmpty(kind)
            && !string.Equals(Context.User?.FindFirst(TokenKinds.TournamentClaim)?.Value,
                tournamentId.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new HubException("Sem acesso a este torneio.");

        // A conexão sobrevive à revogação (o token só é conferido ao conectar): quem
        // reentra no grupo depois de um QR novo/revogado é barrado aqui
        if (kind == TokenKinds.Dealer)
        {
            var sub = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? Context.User?.FindFirst("sub")?.Value;
            if (!Guid.TryParse(sub, out var accessId)
                || !await _dealerAccess.IsActiveAsync(accessId, Context.ConnectionAborted))
                throw new HubException("Acesso de dealer revogado ou expirado.");
        }

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
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Cliente desconectado: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
