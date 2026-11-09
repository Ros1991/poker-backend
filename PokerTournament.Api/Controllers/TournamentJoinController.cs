using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Api.Authorization;
using PokerTournament.Api.Hubs;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Pedido de vaga em torneio público (SPEC-20261108-1639).
/// - Interessado (<c>me/join-requests</c>): qualquer conta logada — rotas sem {tournamentId} (o interessado não é membro
///   do home game; com {tournamentId} o TenantAccessFilter daria 403). Sem [AllowTokenKind]: o dealer por QR não pede (403).
/// - Organizador (<c>tournaments/{tournamentId}/join-requests…</c>): só quem GERENCIA o home game (dono, membro
///   administrador ou Admin do sistema) lista, aprova, recusa e remove reserva — o filtro de tenant deixa passar qualquer
///   membro, por isso a conferência extra. A fila "Reservas" também sai para o dealer por QR (terminal).
/// Depois de aprovar/remover, avisa o grupo do torneio (<c>EntryUpdate</c>) para o painel e o terminal relerem a fila.
/// </summary>
[ApiController]
[Authorize]
public class TournamentJoinController : ControllerBase
{
    private readonly JoinRequestService _service;
    private readonly IAppDbContext _db;
    private readonly IHubContext<TournamentHub> _hub;

    public TournamentJoinController(JoinRequestService service, IAppDbContext db, IHubContext<TournamentHub> hub)
    {
        _service = service;
        _db = db;
        _hub = hub;
    }

    // ---------------------------------------------------------------- interessado

    /// <summary>Estado do botão "Quero participar" da conta logada no torneio (último pedido, inscrita, gerencia, pode pedir).</summary>
    [HttpGet("api/v1/me/join-requests")]
    [ProducesResponseType(typeof(MyJoinStateResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MyJoinStateResponse>> GetMyState([FromQuery] Guid tournamentId, CancellationToken ct)
    {
        if (tournamentId == Guid.Empty)
            return BadRequest(new { message = "Informe o torneio (tournamentId)." });
        return Ok(await _service.GetMyStateAsync(GetUserId(), tournamentId, ct));
    }

    /// <summary>Pedir vaga (uma por torneio) com um recado opcional; quem gerencia o clube é avisado.</summary>
    [HttpPost("api/v1/me/join-requests")]
    [ProducesResponseType(typeof(JoinRequestResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<JoinRequestResponse>> Request([FromBody] CreateJoinRequestRequest request, CancellationToken ct)
    {
        var result = await _service.RequestAsync(GetUserId(), request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Cancelar o próprio pedido (aguardando, reservado ou na espera).</summary>
    [HttpDelete("api/v1/me/join-requests/{id:guid}")]
    public async Task<ActionResult<JoinRequestResponse>> Cancel(Guid id, CancellationToken ct)
    {
        var result = await _service.CancelAsync(GetUserId(), id, ct);
        await NotifyAsync(result.TournamentId);
        return Ok(result);
    }

    // ---------------------------------------------------------------- organizador

    /// <summary>Pedidos aguardando, reservas e lista de espera do torneio, com as vagas.</summary>
    [HttpGet("api/v1/tournaments/{tournamentId:guid}/join-requests")]
    public async Task<ActionResult<TournamentJoinOverviewResponse>> List(Guid tournamentId, CancellationToken ct)
    {
        if (!await ManagesTournamentAsync(tournamentId, ct))
            return Forbidden();
        return Ok(await _service.ListForTournamentAsync(tournamentId, ct));
    }

    /// <summary>A fila "Reservas" (aprovadas sem inscrição): painel e terminal, inclusive o dealer por QR.</summary>
    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpGet("api/v1/tournaments/{tournamentId:guid}/join-requests/reservations")]
    public async Task<ActionResult<List<JoinRequestResponse>>> Reservations(Guid tournamentId, CancellationToken ct)
    {
        return Ok(await _service.ReservationsAsync(tournamentId, ct));
    }

    /// <summary>Aprovar = reservar (jogador garantido no clube, já vinculado); com as vagas esgotadas, lista de espera.</summary>
    [HttpPost("api/v1/tournaments/{tournamentId:guid}/join-requests/{id:guid}/approve")]
    public async Task<ActionResult<JoinRequestResponse>> Approve(Guid tournamentId, Guid id, CancellationToken ct)
    {
        if (!await ManagesTournamentAsync(tournamentId, ct))
            return Forbidden();
        var result = await _service.ApproveAsync(tournamentId, id, GetUserId(), ct);
        await NotifyAsync(tournamentId);
        return Ok(result);
    }

    /// <summary>Recusar (aguardando ou na espera): a conta não pede de novo neste torneio.</summary>
    [HttpPost("api/v1/tournaments/{tournamentId:guid}/join-requests/{id:guid}/decline")]
    public async Task<ActionResult<JoinRequestResponse>> Decline(Guid tournamentId, Guid id, CancellationToken ct)
    {
        if (!await ManagesTournamentAsync(tournamentId, ct))
            return Forbidden();
        var result = await _service.DeclineAsync(tournamentId, id, GetUserId(), ct);
        await NotifyAsync(tournamentId);
        return Ok(result);
    }

    /// <summary>Remover a reserva da fila (a conta pode pedir de novo); a primeira da espera sobe.</summary>
    [HttpPost("api/v1/tournaments/{tournamentId:guid}/join-requests/{id:guid}/remove")]
    public async Task<ActionResult<JoinRequestResponse>> Remove(Guid tournamentId, Guid id, CancellationToken ct)
    {
        if (!await ManagesTournamentAsync(tournamentId, ct))
            return Forbidden();
        var result = await _service.RemoveReservationAsync(tournamentId, id, GetUserId(), ct);
        await NotifyAsync(tournamentId);
        return Ok(result);
    }

    // ---------------------------------------------------------------- apoio

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out var id) ? id : Guid.Empty;
    }

    /// <summary>Dono, membro administrador ou Admin do sistema do home game do torneio (torneio inexistente → 404).</summary>
    private async Task<bool> ManagesTournamentAsync(Guid tournamentId, CancellationToken ct)
    {
        var homeGameId = await _db.Tournaments.Where(t => t.Id == tournamentId).Select(t => (Guid?)t.HomeGameId).FirstOrDefaultAsync(ct);
        if (homeGameId is null)
            throw new KeyNotFoundException();
        var isAdmin = User.FindFirst(ClaimTypes.Role)?.Value == "Admin";
        return await HomeGameAccess.CanManageAsync(_db, homeGameId.Value, GetUserId(), isAdmin, ct);
    }

    private static ObjectResult Forbidden() =>
        new(new { message = "Você não tem acesso a este home game." }) { StatusCode = StatusCodes.Status403Forbidden };

    /// <summary>A fila "Reservas" mudou: o painel e o terminal releem (melhor esforço; a gravação já valeu).</summary>
    private async Task NotifyAsync(Guid tournamentId)
    {
        try
        {
            await _hub.Clients.Group($"tournament_{tournamentId}").SendAsync("EntryUpdate", new { tournamentId, reservations = true });
        }
        catch (Exception) { /* sem tempo real, as telas releem sozinhas */ }
    }
}
