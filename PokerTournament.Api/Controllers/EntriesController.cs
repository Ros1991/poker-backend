using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/[controller]")]
[Authorize]
public class EntriesController : ControllerBase
{
    private readonly EntryService _entryService;

    public EntriesController(EntryService entryService)
    {
        _entryService = entryService;
    }

    [HttpGet]
    public async Task<ActionResult<List<EntryResponse>>> GetAll(
        Guid tournamentId, CancellationToken ct)
    {
        var result = await _entryService.GetEntriesAsync(tournamentId, ct);
        return Ok(result);
    }

    [HttpPost]
    
    public async Task<ActionResult<EntryResponse>> Register(
        Guid tournamentId, [FromBody] CreateEntryRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _entryService.RegisterEntryAsync(tournamentId, request, userId, ct);
        return CreatedAtAction(nameof(GetAll), new { tournamentId }, result);
    }

    [HttpPost("{entryId:guid}/rebuy")]
    
    public async Task<ActionResult<EntryResponse>> Rebuy(
        Guid tournamentId, Guid entryId, [FromBody] RebuyRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _entryService.RegisterRebuyAsync(tournamentId, entryId, request, userId, ct);
        return Ok(result);
    }

    [HttpPost("{entryId:guid}/addon")]
    
    public async Task<ActionResult<EntryResponse>> Addon(
        Guid tournamentId, Guid entryId, [FromBody] AddonRequest? request, CancellationToken ct)
    {
        var userId = GetUserId();
        var isDouble = request?.IsDouble ?? false;
        var result = await _entryService.RegisterAddonAsync(tournamentId, entryId, userId, ct, isDouble);
        return Ok(result);
    }

    [HttpPost("{entryId:guid}/eliminate")]
    
    public async Task<ActionResult<EliminationResultResponse>> Eliminate(
        Guid tournamentId, Guid entryId, [FromBody] EliminateRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _entryService.EliminatePlayerAsync(tournamentId, entryId, request, userId, ct);
        return Ok(result);
    }

    [HttpDelete("{entryId:guid}/rebuy")]
    public async Task<ActionResult<EntryResponse>> RemoveRebuy(
        Guid tournamentId, Guid entryId, CancellationToken ct)
    {
        var result = await _entryService.RemoveRebuyAsync(tournamentId, entryId, ct);
        return Ok(result);
    }

    [HttpDelete("{entryId:guid}/addon")]
    public async Task<ActionResult<EntryResponse>> RemoveAddon(
        Guid tournamentId, Guid entryId, CancellationToken ct)
    {
        var result = await _entryService.RemoveAddonAsync(tournamentId, entryId, ct);
        return Ok(result);
    }

    [HttpPost("{entryId:guid}/undo-elimination")]
    public async Task<ActionResult<EntryResponse>> UndoElimination(
        Guid tournamentId, Guid entryId, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _entryService.UndoEliminationAsync(tournamentId, entryId, userId, ct);
        return Ok(result);
    }

    [HttpDelete("{entryId:guid}")]
    public async Task<ActionResult> Delete(
        Guid tournamentId, Guid entryId, CancellationToken ct)
    {
        await _entryService.DeleteEntryAsync(tournamentId, entryId, ct);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}
