using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Lado do celular logado no pareamento da TV. Reivindicar com {tournamentId} na
/// rota faz o filtro de tenant exigir dono/membro do home game do torneio.
/// </summary>
[ApiController]
[Authorize]
public class DisplaySessionsController : ControllerBase
{
    private readonly DisplayService _display;

    public DisplaySessionsController(DisplayService display)
    {
        _display = display;
    }

    [HttpGet("api/v1/display/pair/{code}")]
    public async Task<ActionResult<PairingCodeLookupResponse>> Lookup(string code, CancellationToken ct)
    {
        return Ok(await _display.LookupCodeAsync(code, GetUserId(), IsAdmin, ct));
    }

    [HttpGet("api/v1/display/my-tournaments")]
    public async Task<ActionResult<List<PairableTournamentResponse>>> MyTournaments(CancellationToken ct)
    {
        return Ok(await _display.GetPairableTournamentsAsync(GetUserId(), IsAdmin, ct));
    }

    /// <summary>TV já pareada: só troca quem tem acesso ao torneio que ela mostra (ver ClaimAsync).</summary>
    [HttpPost("api/v1/tournaments/{tournamentId:guid}/display-sessions/claim")]
    public async Task<ActionResult<DisplayPairedResponse>> Claim(
        Guid tournamentId, [FromBody] CodeRequest request, CancellationToken ct)
    {
        return Ok(await _display.ClaimAsync(request.Code, tournamentId, GetUserId(), IsAdmin, ct));
    }

    /// <summary>Botão "TV Display" do painel: este navegador vira a TV do torneio.</summary>
    [HttpPost("api/v1/tournaments/{tournamentId:guid}/display-sessions")]
    public async Task<ActionResult<DisplayDeviceTokenResponse>> PairThisBrowser(Guid tournamentId, CancellationToken ct)
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        return Ok(await _display.PairDirectAsync(tournamentId, GetUserId(), userAgent, ct));
    }

    private bool IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "Admin";

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}
