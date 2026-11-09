using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokerTournament.Api.Hubs;
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
    private readonly IHubContext<TournamentHub> _hubContext;

    public TournamentsController(TournamentService tournamentService, IHubContext<TournamentHub> hubContext)
    {
        _tournamentService = tournamentService;
        _hubContext = hubContext;
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
        await _tournamentService.EnsureInHomeGameAsync(homeGameId, id, ct);
        var result = await _tournamentService.GetByIdAsync(id, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TournamentResponse>> Create(
        Guid homeGameId, [FromBody] CreateTournamentRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        // Limite de torneios do plano do dono (SPEC-20261023-1205) — o Admin do sistema também segue (SPEC-20261026-1126).
        // Os campos de divulgação só valem se quem cria gerencia o home game (revisão da SPEC-20261108-1639)
        var result = await _tournamentService.CreateAsync(homeGameId, request, userId, IsAdmin, ct);
        return CreatedAtAction(nameof(GetById), new { homeGameId, id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TournamentResponse>> Update(
        Guid homeGameId, Guid id, [FromBody] CreateTournamentRequest request, CancellationToken ct)
    {
        await _tournamentService.EnsureInHomeGameAsync(homeGameId, id, ct);
        // Membro comum edita o torneio, mas os campos de divulgação do corpo são ignorados (revisão da SPEC-20261108-1639)
        var result = await _tournamentService.UpdateAsync(id, request, GetUserId(), IsAdmin, ct);
        // Torneio iniciado também se edita (SPEC-20261103-1927): o terminal e as outras telas releem o torneio e o devido
        // dos jogadores (que muda com os valores). Melhor esforço: a edição já foi gravada
        try
        {
            var group = _hubContext.Clients.Group($"tournament_{id}");
            await group.SendAsync("TournamentUpdate", new { tournamentId = id, status = result.Status }, ct);
            await group.SendAsync("EntryUpdate", new { tournamentId = id }, ct);
        }
        catch (Exception) { /* sem tempo real, as telas releem sozinhas */ }
        return Ok(result);
    }

    [HttpGet("{id:guid}/blind-levels")]
    public async Task<ActionResult<List<TournamentBlindLevelResponse>>> GetBlindLevels(
        Guid homeGameId, Guid id, CancellationToken ct)
    {
        await _tournamentService.EnsureInHomeGameAsync(homeGameId, id, ct);
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
        await _tournamentService.EnsureInHomeGameAsync(homeGameId, id, ct);
        var userId = GetUserId();
        var result = await _tournamentService.UpdateStatusAsync(id, request.Status, userId, ct);
        // O terminal do dealer troca o "Iniciar torneio" pelo Pausar sem esperar a próxima leitura (SPEC-20261103-1927)
        try
        {
            await _hubContext.Clients.Group($"tournament_{id}")
                .SendAsync("TournamentUpdate", new { tournamentId = id, status = result.Status }, ct);
        }
        catch (Exception) { /* melhor esforço */ }
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid homeGameId, Guid id, CancellationToken ct)
    {
        await _tournamentService.EnsureInHomeGameAsync(homeGameId, id, ct);
        await _tournamentService.DeleteAsync(id, ct);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }

    private bool IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "Admin";
}

public class UpdateStatusRequest
{
    public string Status { get; set; } = string.Empty;
}
