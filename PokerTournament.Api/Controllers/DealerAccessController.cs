using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PokerTournament.Api.Authorization;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// QR do dealer. Gerar/ver/revogar: quem administra o torneio (o {tournamentId} na
/// rota passa pelo filtro de tenant; token de dealer não chega aqui). Trocar o
/// código: anônimo, com limite por IP.
/// </summary>
[ApiController]
[Authorize]
public class DealerAccessController : ControllerBase
{
    private readonly DealerAccessService _service;

    public DealerAccessController(DealerAccessService service)
    {
        _service = service;
    }

    [HttpPost("api/v1/tournaments/{tournamentId:guid}/dealer-access")]
    public async Task<ActionResult<DealerAccessResponse>> Create(Guid tournamentId, CancellationToken ct)
    {
        return Ok(await _service.CreateAsync(tournamentId, GetUserId(), ct));
    }

    [HttpGet("api/v1/tournaments/{tournamentId:guid}/dealer-access")]
    public async Task<ActionResult<DealerAccessResponse>> GetActive(Guid tournamentId, CancellationToken ct)
    {
        var access = await _service.GetActiveAsync(tournamentId, ct);
        return access is null ? NoContent() : Ok(access);
    }

    [HttpDelete("api/v1/tournaments/{tournamentId:guid}/dealer-access")]
    public async Task<ActionResult> Revoke(Guid tournamentId, CancellationToken ct)
    {
        await _service.RevokeAsync(tournamentId, ct);
        return NoContent();
    }

    [HttpPost("api/v1/dealer-access/exchange")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    public async Task<ActionResult<DealerExchangeResponse>> Exchange(
        [FromBody] CodeRequest request, CancellationToken ct)
    {
        return Ok(await _service.ExchangeAsync(request.Code, ct));
    }

    /// <summary>O terminal confere a sessão do dealer ao abrir (no lugar do /auth/me).</summary>
    [HttpGet("api/v1/tournaments/{tournamentId:guid}/dealer-access/session")]
    [AllowTokenKind(TokenKinds.Dealer)]
    public async Task<ActionResult<DealerSessionResponse>> Session(Guid tournamentId, CancellationToken ct)
    {
        if (User.FindFirst(TokenKinds.ClaimType)?.Value != TokenKinds.Dealer
            || GetUserId() is not { } accessId)
            return NotFound(new { message = "Sessão de dealer não encontrada." });

        var session = await _service.GetSessionAsync(accessId, ct);
        return session is null ? NotFound(new { message = "Sessão de dealer não encontrada." }) : Ok(session);
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out var id) ? id : null;
    }
}
