using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/home-games/{homeGameId:guid}/rankings")]
[Authorize]
public class HomeGameRankingsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;
    private readonly RankingLeaderboardService _leaderboard;
    private readonly RankingSlugService _slugs;

    public HomeGameRankingsController(
        IAppDbContext db, IMapper mapper, RankingLeaderboardService leaderboard, RankingSlugService slugs)
    {
        _db = db;
        _mapper = mapper;
        _leaderboard = leaderboard;
        _slugs = slugs;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(Guid homeGameId, CancellationToken ct)
    {
        var rankings = await _db.Rankings
            .Where(r => r.HomeGameId == homeGameId && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Slug,
                r.Description,
                r.Season,
                r.StartDate,
                r.EndDate,
                r.ScoringMode,
                r.ScoringFormula,
                r.ScoringTable,
                r.AccumulatedPrize,
                r.DiscardCount,
                r.IsActive,
                TournamentCount = r.Tournaments.Count,
                r.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(rankings);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> GetById(Guid homeGameId, Guid id, CancellationToken ct)
    {
        var ranking = await _db.Rankings
            .FirstOrDefaultAsync(r => r.Id == id && r.HomeGameId == homeGameId, ct);

        if (ranking is null)
            return NotFound(new { message = "Ranking não encontrado." });

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

    [HttpPost]
    public async Task<ActionResult> Create(
        Guid homeGameId, [FromBody] CreateRankingRequest request, CancellationToken ct)
    {
        // Verificar permissão: owner do HG, admin do HG, ou admin do sistema
        var userId = GetUserId();
        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == homeGameId, ct);
        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (homeGame.OwnerId != userId && userRole != "Admin")
        {
            var isHgAdmin = await _db.HomeGameMembers
                .AnyAsync(m => m.HomeGameId == homeGameId && m.UserId == userId && m.IsAdmin, ct);
            if (!isHgAdmin)
                return Forbid();
        }

        var ranking = _mapper.Map<Ranking>(request);
        ranking.HomeGameId = homeGameId;
        ranking.ScoringMode = request.ScoringMode;
        ranking.ScoringFormula = request.ScoringFormula;
        ranking.ScoringTable = request.ScoringTable;
        ranking.DiscardCount = request.DiscardCount ?? 0;
        ranking.Slug = await _slugs.NextAvailableAsync(ranking.Name, ct);

        _db.Rankings.Add(ranking);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/rankings/{ranking.Id}",
            new { ranking.Id, ranking.Name, ranking.Slug });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(
        Guid homeGameId, Guid id, [FromBody] CreateRankingRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == homeGameId, ct);
        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (homeGame.OwnerId != userId && userRole != "Admin")
        {
            var isHgAdmin = await _db.HomeGameMembers
                .AnyAsync(m => m.HomeGameId == homeGameId && m.UserId == userId && m.IsAdmin, ct);
            if (!isHgAdmin)
                return Forbid();
        }

        var ranking = await _db.Rankings
            .FirstOrDefaultAsync(r => r.Id == id && r.HomeGameId == homeGameId, ct);

        if (ranking is null)
            return NotFound(new { message = "Ranking não encontrado." });

        ranking.Name = request.Name;
        ranking.Description = request.Description;
        ranking.Season = request.Season;
        ranking.ScoringRuleId = request.ScoringRuleId;
        ranking.StartDate = request.StartDate;
        ranking.EndDate = request.EndDate;
        ranking.ScoringMode = request.ScoringMode;
        ranking.ScoringFormula = request.ScoringFormula;
        ranking.ScoringTable = request.ScoringTable;
        if (request.AccumulatedPrize.HasValue)
            ranking.AccumulatedPrize = request.AccumulatedPrize.Value;
        if (request.DiscardCount.HasValue)
            ranking.DiscardCount = request.DiscardCount.Value;

        await _db.SaveChangesAsync(ct);
        return Ok(new { ranking.Id, ranking.Name });
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid homeGameId, Guid id, CancellationToken ct)
    {
        // SEC-03: mesma checagem de permissão do Create/Update (antes não havia nenhuma)
        var userId = GetUserId();
        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == homeGameId, ct);
        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (homeGame.OwnerId != userId && userRole != "Admin")
        {
            var isHgAdmin = await _db.HomeGameMembers
                .AnyAsync(m => m.HomeGameId == homeGameId && m.UserId == userId && m.IsAdmin, ct);
            if (!isHgAdmin)
                return Forbid();
        }

        var ranking = await _db.Rankings
            .FirstOrDefaultAsync(r => r.Id == id && r.HomeGameId == homeGameId, ct);

        if (ranking is null)
            return NotFound(new { message = "Ranking não encontrado." });

        ranking.IsActive = false;
        ranking.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpGet("{id:guid}/leaderboard")]
    public async Task<ActionResult<List<RankingLeaderboardResponse>>> GetLeaderboard(
        Guid homeGameId, Guid id, CancellationToken ct)
    {
        var ranking = await _db.Rankings
            .FirstOrDefaultAsync(r => r.Id == id && r.HomeGameId == homeGameId, ct);
        if (ranking is null)
            return NotFound(new { message = "Ranking não encontrado." });

        return Ok(await _leaderboard.GetLeaderboardAsync(ranking, ct));
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}
