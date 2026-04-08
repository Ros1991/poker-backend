using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokerTournament.Api.Hubs;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/[controller]")]
[Authorize]
public class TimerController : ControllerBase
{
    private readonly TimerService _timerService;
    private readonly IHubContext<TournamentHub> _hubContext;

    public TimerController(TimerService timerService, IHubContext<TournamentHub> hubContext)
    {
        _timerService = timerService;
        _hubContext = hubContext;
    }

    [HttpGet]
    public async Task<ActionResult<TimerStateResponse>> GetState(
        Guid tournamentId, CancellationToken ct)
    {
        var result = await _timerService.GetTimerStateAsync(tournamentId, ct);
        return Ok(result);
    }

    [HttpPost("start")]
    
    public async Task<ActionResult> Start(Guid tournamentId, CancellationToken ct)
    {
        await _timerService.StartTimerAsync(tournamentId, ct);
        var state = await _timerService.GetTimerStateAsync(tournamentId, ct);
        await NotifyTimerChange(tournamentId, state);
        return Ok(state);
    }

    [HttpPost("pause")]
    
    public async Task<ActionResult> Pause(Guid tournamentId, CancellationToken ct)
    {
        await _timerService.PauseTimerAsync(tournamentId, ct);
        var state = await _timerService.GetTimerStateAsync(tournamentId, ct);
        await NotifyTimerChange(tournamentId, state);
        return Ok(state);
    }

    [HttpPost("resume")]
    
    public async Task<ActionResult> Resume(Guid tournamentId, CancellationToken ct)
    {
        await _timerService.ResumeTimerAsync(tournamentId, ct);
        var state = await _timerService.GetTimerStateAsync(tournamentId, ct);
        await NotifyTimerChange(tournamentId, state);
        return Ok(state);
    }

    [HttpPost("next")]
    
    public async Task<ActionResult<TimerStateResponse>> NextLevel(Guid tournamentId, CancellationToken ct)
    {
        var userId = GetUserId();
        var state = await _timerService.AdvanceLevelAsync(tournamentId, userId, ct);
        await _hubContext.Clients
            .Group($"tournament_{tournamentId}")
            .SendAsync("BlindLevelChanged", state, ct);
        return Ok(state);
    }

    [HttpPost("previous")]
    
    public async Task<ActionResult<TimerStateResponse>> PreviousLevel(Guid tournamentId, CancellationToken ct)
    {
        var userId = GetUserId();
        var state = await _timerService.PreviousLevelAsync(tournamentId, userId, ct);
        await _hubContext.Clients
            .Group($"tournament_{tournamentId}")
            .SendAsync("BlindLevelChanged", state, ct);
        return Ok(state);
    }

    private async Task NotifyTimerChange(Guid tournamentId, TimerStateResponse state)
    {
        await _hubContext.Clients
            .Group($"tournament_{tournamentId}")
            .SendAsync("TimerStateChanged", state);
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}
