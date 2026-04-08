using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;

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

    public RankingsController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> GetById(Guid id, CancellationToken ct)
    {
        var ranking = await _db.Rankings
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (ranking is null)
            return NotFound(new { message = "Ranking não encontrado." });

        return Ok(new
        {
            ranking.Id,
            ranking.HomeGameId,
            ranking.Name,
            ranking.Description,
            ranking.Season,
            ranking.IsActive,
            ranking.ScoringMode,
            ranking.ScoringFormula,
            ranking.ScoringTable,
            ranking.AccumulatedPrize,
            ranking.CreatedAt
        });
    }

    [HttpGet("{id:guid}/leaderboard")]
    public async Task<ActionResult<List<RankingLeaderboardResponse>>> GetLeaderboard(
        Guid id, CancellationToken ct)
    {
        var scores = await _db.RankingScores
            .Include(rs => rs.Person)
            .Where(rs => rs.RankingId == id)
            .ToListAsync(ct);

        var leaderboard = scores
            .GroupBy(s => s.PersonId)
            .Select(g => new RankingLeaderboardResponse
            {
                Person = new PersonResponse
                {
                    Id = g.First().Person.Id,
                    FullName = g.First().Person.FullName,
                    Nickname = g.First().Person.Nickname,
                    PhotoUrl = g.First().Person.PhotoUrl,
                    IsActive = g.First().Person.IsActive
                },
                TotalPoints = g.Sum(s => s.TotalPoints),
                TournamentsPlayed = g.Count(),
                BestPosition = g.Min(s => s.Position)
            })
            .OrderByDescending(l => l.TotalPoints)
            .ThenBy(l => l.BestPosition)
            .ToList();

        for (int i = 0; i < leaderboard.Count; i++)
            leaderboard[i].Position = i + 1;

        return Ok(leaderboard);
    }
}
