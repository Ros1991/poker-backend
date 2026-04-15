using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

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

        // Busca torneios finalizados do ranking
        var tournamentIds = await _db.Tournaments
            .Where(t => t.RankingId == id && t.Status == "Finished")
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (tournamentIds.Count == 0)
            return Ok(new List<RankingLeaderboardResponse>());

        // Busca entries com posição final
        var entries = await _db.TournamentEntries
            .Include(e => e.Person)
            .Where(e => tournamentIds.Contains(e.TournamentId)
                     && e.FinalPosition != null
                     && (e.Status == "Eliminated" || e.Status == "Awarded"))
            .ToListAsync(ct);

        if (entries.Count == 0)
            return Ok(new List<RankingLeaderboardResponse>());

        var playerCounts = entries
            .GroupBy(e => e.TournamentId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Parse tabela de pontuação
        List<PositionPoints>? table = null;
        if (ranking.ScoringMode == "Table" && !string.IsNullOrWhiteSpace(ranking.ScoringTable))
        {
            try
            {
                table = System.Text.Json.JsonSerializer.Deserialize<List<PositionPoints>>(
                    ranking.ScoringTable,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { table = null; }
        }

        var scores = entries.Select(e =>
        {
            var pos = e.FinalPosition ?? 0;
            var total = playerCounts.GetValueOrDefault(e.TournamentId, 0);
            decimal points = CalculatePoints(ranking, table, pos, total);
            return new { e.PersonId, Person = e.Person, e.TournamentId, Position = pos, Points = points };
        }).ToList();

        var discard = Math.Max(0, ranking.DiscardCount);

        var leaderboard = scores
            .GroupBy(s => s.PersonId)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(s => s.Points).ToList();
                var keep = ordered.Take(Math.Max(0, ordered.Count - discard)).ToList();
                var first = g.First();
                return new RankingLeaderboardResponse
                {
                    Person = new PersonResponse
                    {
                        Id = first.Person.Id,
                        FullName = first.Person.FullName,
                        Nickname = first.Person.Nickname,
                        PhotoUrl = first.Person.PhotoUrl,
                        IsActive = first.Person.IsActive
                    },
                    TotalPoints = keep.Sum(s => s.Points),
                    TournamentsPlayed = ordered.Count,
                    BestPosition = g.Min(s => s.Position)
                };
            })
            .OrderByDescending(l => l.TotalPoints)
            .ThenBy(l => l.BestPosition)
            .ToList();

        for (int i = 0; i < leaderboard.Count; i++)
            leaderboard[i].Position = i + 1;

        return Ok(leaderboard);
    }

    private static decimal CalculatePoints(Ranking ranking, List<PositionPoints>? table, int position, int playerCount)
    {
        if (ranking.ScoringMode == "Table" && table != null)
        {
            var row = table.FirstOrDefault(p => p.Position == position);
            return row?.Points ?? 0m;
        }
        return Math.Max(0, playerCount - position + 1);
    }

    public class PositionPoints
    {
        public int Position { get; set; }
        public decimal Points { get; set; }
    }
}
