using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Vínculo pelo jogador (SPEC-20261108-1632): convite do torneio (QR/link) e pedidos "sou eu".
/// - Gerar/ver o convite: quem acessa o torneio (o {tournamentId} na rota passa pelo TenantAccessFilter: dono, membro
///   ou Admin). Sem [AllowTokenKind]: o dealer por QR não convida (403 do TokenKindHandler).
/// - Página do convite (GET invite/{code}): anônima, com limite por IP — só nome de exibição e foto dos inscritos sem
///   conta. "Meu pedido" e o claim exigem conta logada (token de dealer → 403).
/// - Listar/aprovar/recusar pedidos: só quem GERENCIA o home game (dono, membro administrador ou Admin do sistema) —
///   o filtro de tenant deixa passar qualquer membro, por isso a conferência extra aqui.
/// </summary>
[ApiController]
[Authorize]
public class PlayerLinkController : ControllerBase
{
    private readonly PlayerLinkRequestService _service;
    private readonly IAppDbContext _db;

    public PlayerLinkController(PlayerLinkRequestService service, IAppDbContext db)
    {
        _service = service;
        _db = db;
    }

    // ---------------------------------------------------------------- convite (organizador)

    /// <summary>Gera (ou renova) o convite do torneio: o anterior deixa de valer.</summary>
    [HttpPost("api/v1/tournaments/{tournamentId:guid}/invite")]
    public async Task<ActionResult<TournamentInviteResponse>> CreateInvite(Guid tournamentId, CancellationToken ct)
    {
        return Ok(await _service.CreateInviteAsync(tournamentId, GetUserId(), ct));
    }

    /// <summary>Convite ativo do torneio (204 sem convite).</summary>
    [HttpGet("api/v1/tournaments/{tournamentId:guid}/invite")]
    public async Task<ActionResult<TournamentInviteResponse>> GetInvite(Guid tournamentId, CancellationToken ct)
    {
        var invite = await _service.GetActiveInviteAsync(tournamentId, ct);
        return invite is null ? NoContent() : Ok(invite);
    }

    // ---------------------------------------------------------------- página do convite (jogador)

    /// <summary>O torneio e os inscritos ainda sem conta (nome de exibição e foto), para quem leu o QR — sem login.</summary>
    [HttpGet("api/v1/invite/{code}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    public async Task<ActionResult<InviteInfoResponse>> GetInviteInfo(string code, CancellationToken ct)
    {
        return Ok(await _service.GetInviteInfoAsync(code, ct));
    }

    /// <summary>O último pedido da conta logada neste torneio (e se ela já é um inscrito vinculado).</summary>
    [HttpGet("api/v1/invite/{code}/my-request")]
    public async Task<ActionResult<MyLinkRequestResponse>> GetMyRequest(string code, CancellationToken ct)
    {
        if (GetUserId() is not { } userId)
            return Unauthorized();
        return Ok(await _service.GetMyRequestAsync(code, userId, ct));
    }

    /// <summary>"Sou eu" (personId) ou "não estou na lista" (notInList): cria o pedido e avisa quem gerencia.</summary>
    [HttpPost("api/v1/invite/{code}/claim")]
    public async Task<ActionResult<PlayerLinkRequestResponse>> Claim(
        string code, [FromBody] ClaimInviteRequest request, CancellationToken ct)
    {
        if (GetUserId() is not { } userId)
            return Unauthorized();
        return Ok(await _service.ClaimAsync(code, userId, request, ct));
    }

    // ---------------------------------------------------------------- organizador

    /// <summary>Pedidos aguardando neste torneio (com o e-mail da conta, para cadastrar quem não está na lista).</summary>
    [HttpGet("api/v1/tournaments/{tournamentId:guid}/link-requests")]
    public async Task<ActionResult<List<PlayerLinkRequestResponse>>> ListRequests(Guid tournamentId, CancellationToken ct)
    {
        if (!await ManagesTournamentAsync(tournamentId, ct))
            return Forbidden();
        return Ok(await _service.ListPendingAsync(tournamentId, ct));
    }

    /// <summary>Aprova: grava Person.UserId e copia o e-mail da conta para o cadastro sem e-mail.</summary>
    [HttpPost("api/v1/tournaments/{tournamentId:guid}/link-requests/{id:guid}/approve")]
    public async Task<ActionResult<PlayerLinkRequestResponse>> Approve(
        Guid tournamentId, Guid id, [FromBody] ApproveLinkRequest? request, CancellationToken ct)
    {
        if (!await ManagesTournamentAsync(tournamentId, ct))
            return Forbidden();
        return Ok(await _service.ApproveAsync(tournamentId, id, GetUserId(), request?.PersonId, ct));
    }

    /// <summary>Recusa (ou dispensa o aviso "não estou na lista"); o jogador pode pedir de novo.</summary>
    [HttpPost("api/v1/tournaments/{tournamentId:guid}/link-requests/{id:guid}/decline")]
    public async Task<ActionResult<PlayerLinkRequestResponse>> Decline(Guid tournamentId, Guid id, CancellationToken ct)
    {
        if (!await ManagesTournamentAsync(tournamentId, ct))
            return Forbidden();
        return Ok(await _service.DeclineAsync(tournamentId, id, GetUserId(), ct));
    }

    // ---------------------------------------------------------------- apoio

    /// <summary>Dono, membro administrador ou Admin do sistema do home game do torneio (torneio inexistente → 404 no serviço).</summary>
    private async Task<bool> ManagesTournamentAsync(Guid tournamentId, CancellationToken ct)
    {
        var homeGameId = await _db.Tournaments.Where(t => t.Id == tournamentId).Select(t => (Guid?)t.HomeGameId).FirstOrDefaultAsync(ct);
        if (homeGameId is null)
            throw new KeyNotFoundException();
        var isAdmin = User.FindFirst(ClaimTypes.Role)?.Value == "Admin";
        return await HomeGameAccess.CanManageAsync(_db, homeGameId.Value, GetUserId() ?? Guid.Empty, isAdmin, ct);
    }

    private static ObjectResult Forbidden() =>
        new(new { message = "Só quem gerencia o home game pode ver e responder aos pedidos de vínculo." }) { StatusCode = 403 };

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out var id) ? id : null;
    }
}
