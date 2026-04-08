using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PokerTournament.Api.Hubs;

[Authorize]
public class TournamentHub : Hub
{
    private readonly ILogger<TournamentHub> _logger;

    public TournamentHub(ILogger<TournamentHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinTournament(Guid tournamentId)
    {
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
