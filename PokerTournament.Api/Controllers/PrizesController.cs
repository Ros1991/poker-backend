using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/[controller]")]
[Authorize]
public class PrizesController : ControllerBase
{
    private readonly PrizeService _prizeService;
    private readonly IAppDbContext _db;

    public PrizesController(PrizeService prizeService, IAppDbContext db)
    {
        _prizeService = prizeService;
        _db = db;
    }

    [HttpGet("calculate")]
    public async Task<ActionResult<PrizeCalculationResponse>> Calculate(
        Guid tournamentId, [FromQuery] int? prizeCount, CancellationToken ct)
    {
        var result = await _prizeService.CalculatePrizesAsync(tournamentId, prizeCount, ct);
        return Ok(result);
    }

    [HttpPost("confirm")]
    
    public async Task<ActionResult<List<PrizeAllocation>>> Confirm(
        Guid tournamentId, [FromBody] ConfirmPrizesRequest request, CancellationToken ct)
    {
        var result = await _prizeService.ConfirmPrizesAsync(tournamentId, request, ct);
        return Ok(result);
    }

    [HttpPost("{prizeId:guid}/pay")]
    
    public async Task<ActionResult> Pay(Guid tournamentId, Guid prizeId, CancellationToken ct)
    {
        var prize = await _db.TournamentPrizes
            .FirstOrDefaultAsync(p => p.Id == prizeId && p.TournamentId == tournamentId, ct);

        if (prize is null)
            return NotFound(new { message = "Prêmio não encontrado." });

        if (prize.Paid)
            return BadRequest(new { message = "Prêmio já foi pago." });

        prize.Paid = true;
        prize.PaidAt = DateTimeOffset.UtcNow;

        // Atualizar entry se vinculado
        if (prize.EntryId.HasValue)
        {
            var entry = await _db.TournamentEntries
                .FirstOrDefaultAsync(e => e.Id == prize.EntryId, ct);

            if (entry is not null)
            {
                entry.PrizePaid = true;
                entry.Status = nameof(EntryStatus.PaidOut);
            }
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
