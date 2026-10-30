using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Api.Authorization;
using PokerTournament.Application.Common;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Hubs;

[Authorize]
// Dealer por QR conecta; JoinTournament restringe ao torneio do token
[AllowTokenKind(TokenKinds.Dealer, TournamentRouteKey = null)]
public class TournamentHub : Hub
{
    private const string NoAccessMessage = "Sem acesso a este torneio.";

    private readonly ILogger<TournamentHub> _logger;
    private readonly DealerAccessService _dealerAccess;
    private readonly DealerConnectionRegistry _dealerConnections;
    private readonly IAppDbContext _db;

    public TournamentHub(
        ILogger<TournamentHub> logger,
        DealerAccessService dealerAccess,
        DealerConnectionRegistry dealerConnections,
        IAppDbContext db)
    {
        _logger = logger;
        _dealerAccess = dealerAccess;
        _dealerConnections = dealerConnections;
        _db = db;
    }

    // Acesso do dealer (pedido revogado, QR desativado, prazo vencido, torneio terminado) é
    // revalidado em TODA chamada pelo DealerHubFilter, antes de chegar aqui.
    // Conta logada (SPEC-20261029-2316): só entra no grupo de torneio a que tem acesso — Admin do sistema, dono ou
    // membro do home game do torneio, a mesma regra do TenantAccessFilter das rotas REST. Antes qualquer conta entrava
    // no grupo de qualquer torneio sabendo o id.
    public async Task JoinTournament(Guid tournamentId)
    {
        var user = Context.User;
        var kind = user?.FindFirst(TokenKinds.ClaimType)?.Value;
        if (!string.IsNullOrEmpty(kind))
        {
            if (!string.Equals(user?.FindFirst(TokenKinds.TournamentClaim)?.Value,
                    tournamentId.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new HubException(NoAccessMessage);
        }
        else if (!await UserCanJoinAsync(user, tournamentId))
        {
            _logger.LogWarning("Conexão {ConnectionId} recusada no grupo do torneio {TournamentId}: sem acesso",
                Context.ConnectionId, tournamentId);
            throw new HubException(NoAccessMessage);
        }

        var groupName = $"tournament_{tournamentId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Conexão {ConnectionId} entrou no grupo {Group}", Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Conta logada (token de usuário): a regra do <see cref="HomeGameAccess.CanAccessAsync"/> (Admin do sistema, dono ou
    /// membro do home game do torneio). O torneio é procurado também entre os excluídos logicamente, como no
    /// TenantAccessFilter; torneio que nunca existiu recusa (não há grupo útil).
    /// </summary>
    private async Task<bool> UserCanJoinAsync(ClaimsPrincipal? user, Guid tournamentId)
    {
        var ct = Context.ConnectionAborted;
        var homeGameId = await _db.Tournaments
            .IgnoreQueryFilters()
            .Where(t => t.Id == tournamentId)
            .Select(t => (Guid?)t.HomeGameId)
            .FirstOrDefaultAsync(ct);
        if (homeGameId is null) return false;

        var userIdText = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user?.FindFirst("sub")?.Value;
        if (!Guid.TryParse(userIdText, out var userId)) return false;

        var isAdmin = user?.FindFirst(ClaimTypes.Role)?.Value == "Admin";
        return await HomeGameAccess.CanAccessAsync(_db, homeGameId.Value, userId, isAdmin, ct);
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
