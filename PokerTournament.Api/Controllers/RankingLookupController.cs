using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Endpoints standalone para ranking (sem precisar de homeGameId na URL)
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class RankingsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly RankingLeaderboardService _leaderboard;
    private readonly RankingSlugService _slugs;

    public RankingsController(IAppDbContext db, RankingLeaderboardService leaderboard, RankingSlugService slugs)
    {
        _db = db;
        _leaderboard = leaderboard;
        _slugs = slugs;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> GetById(Guid id, CancellationToken ct)
    {
        var ranking = await _db.Rankings
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (ranking is null)
            return NotFound(new { message = "Ranking não encontrado." });

        // Rede de segurança: se o backfill do boot falhou (ou o ranking veio de uma
        // instância antiga durante o deploy), gera o endereço público ao abrir a tela.
        if (ranking.Slug is null)
        {
            try
            {
                ranking.Slug = await _slugs.NextAvailableAsync(ranking.Name, ct);
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Outra requisição gerou ao mesmo tempo; na próxima abertura ele aparece
                ranking.Slug = null;
            }
        }

        return Ok(new
        {
            ranking.Id,
            ranking.HomeGameId,
            ranking.Name,
            ranking.Slug,
            ranking.Description,
            ranking.Season,
            ranking.IsActive,
            ranking.ScoringMode,
            ranking.ScoringFormula,
            ranking.ScoringTable,
            ranking.AccumulatedPrize,
            ranking.DiscardCount,
            ranking.CreatedAt
        });
    }

    [HttpGet("{id:guid}/leaderboard")]
    public async Task<ActionResult<List<RankingLeaderboardResponse>>> GetLeaderboard(
        Guid id, CancellationToken ct)
    {
        var ranking = await _db.Rankings
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ranking is null)
            return NotFound(new { message = "Ranking não encontrado." });

        return Ok(await _leaderboard.GetLeaderboardAsync(ranking, ct));
    }
}
