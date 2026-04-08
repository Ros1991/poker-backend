using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/home-games/{homeGameId:guid}/[controller]")]
[Authorize]
public class TournamentsController : ControllerBase
{
    private readonly TournamentService _tournamentService;

    public TournamentsController(TournamentService tournamentService)
    {
        _tournamentService = tournamentService;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<TournamentResponse>>> GetAll(
        Guid homeGameId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _tournamentService.GetAllAsync(homeGameId, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TournamentResponse>> GetById(Guid homeGameId, Guid id, CancellationToken ct)
    {
        var result = await _tournamentService.GetByIdAsync(id, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TournamentResponse>> Create(
        Guid homeGameId, [FromBody] CreateTournamentRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _tournamentService.CreateAsync(homeGameId, request, userId, ct);
        return CreatedAtAction(nameof(GetById), new { homeGameId, id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TournamentResponse>> Update(
        Guid homeGameId, Guid id, [FromBody] CreateTournamentRequest request, CancellationToken ct)
    {
        var result = await _tournamentService.UpdateAsync(id, request, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/blind-levels")]
    public async Task<ActionResult<List<TournamentBlindLevelResponse>>> GetBlindLevels(
        Guid homeGameId, Guid id, CancellationToken ct)
    {
        var result = await _tournamentService.GetBlindLevelsAsync(id, ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/blind-levels")]
    public async Task<ActionResult> UpdateBlindLevels(
        Guid homeGameId, Guid id, [FromBody] UpdateTournamentBlindLevelsRequest request, CancellationToken ct)
    {
        await _tournamentService.UpdateBlindLevelsAsync(homeGameId, id, request, ct);
        return Ok(new { message = "Blinds atualizados." });
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<TournamentResponse>> UpdateStatus(
        Guid homeGameId, Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _tournamentService.UpdateStatusAsync(id, request.Status, userId, ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}

public class UpdateStatusRequest
{
    public string Status { get; set; } = string.Empty;
}
