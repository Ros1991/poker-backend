using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Telão sem login. A TV se identifica pelo header X-Display-Token (o token do
/// aparelho, recebido uma vez na criação). Sem {homeGameId}/{tournamentId} na rota:
/// com eles o filtro de tenant devolveria 403 para quem estivesse logado no navegador.
/// </summary>
[ApiController]
[Route("api/v1/public/display")]
[AllowAnonymous]
public class PublicDisplayController : ControllerBase
{
    private const string TokenHeader = "X-Display-Token";
    private static readonly object InvalidDevice = new { message = "TV não reconhecida. Gere um novo QR." };

    private readonly DisplayService _display;

    public PublicDisplayController(DisplayService display)
    {
        _display = display;
    }

    [HttpPost("sessions")]
    [EnableRateLimiting("public-anon")]
    public async Task<ActionResult<DisplaySessionCreatedResponse>> Create(CancellationToken ct)
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        return Ok(await _display.CreateAsync(userAgent, ct));
    }

    [HttpGet("session")]
    public async Task<ActionResult<DisplaySessionStatusResponse>> Status(CancellationToken ct)
    {
        var status = await _display.GetStatusAsync(DeviceToken, ct);
        return status is null ? Unauthorized(InvalidDevice) : Ok(status);
    }

    /// <summary>Botão "Voltar": a TV sai do torneio e volta para o QR grande.</summary>
    [HttpPost("session/leave")]
    public async Task<ActionResult<DisplaySessionStatusResponse>> Leave(CancellationToken ct)
    {
        var status = await _display.LeaveAsync(DeviceToken, ct);
        return status is null ? Unauthorized(InvalidDevice) : Ok(status);
    }

    [HttpGet("session/snapshot")]
    public async Task<ActionResult<TvSnapshotResponse>> Snapshot(CancellationToken ct)
    {
        var (outcome, snapshot) = await _display.GetSnapshotAsync(DeviceToken, ct);
        return outcome switch
        {
            SnapshotOutcome.InvalidDevice => Unauthorized(InvalidDevice),
            SnapshotOutcome.NotPaired => Conflict(new { message = "A TV não está mostrando nenhum torneio." }),
            _ => Ok(snapshot),
        };
    }

    private string? DeviceToken => Request.Headers[TokenHeader].ToString() is { Length: > 0 } token ? token : null;
}
