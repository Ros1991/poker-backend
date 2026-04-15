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
        var totalEtapas = tournamentIds.Count;
        // Slots a manter = totalEtapas - descarte (faltas contam como 0)
        var slotsToKeep = Math.Max(1, totalEtapas - discard);

        var leaderboard = scores
            .GroupBy(s => s.PersonId)
            .Select(g =>
            {
                // Pontos reais em ordem decrescente
                var realPoints = g.OrderByDescending(s => s.Points).Select(s => s.Points).ToList();
                // Preencher faltas como 0 até totalEtapas
                var absences = totalEtapas - realPoints.Count;
                for (int i = 0; i < absences; i++) realPoints.Add(0m);
                // Manter apenas os N melhores (descartando piores, incluindo faltas)
                var kept = realPoints.Take(slotsToKeep).ToList();
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
                    TotalPoints = kept.Sum(),
                    TournamentsPlayed = g.Count(),
                    BestPosition = g.Min(s => s.Position),
                    DiscardedPoints = realPoints.Sum() - kept.Sum(),
                    AveragePoints = g.Count() > 0 ? Math.Round(g.Sum(s => s.Points) / g.Count(), 2) : 0
                };
            })
            .OrderByDescending(l => l.TotalPoints)
            .ThenByDescending(l => l.AveragePoints)
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

        // Avaliar fórmula JavaScript customizada
        if (!string.IsNullOrWhiteSpace(ranking.ScoringFormula))
        {
            try
            {
                var engine = new Jint.Engine();
                engine.SetValue("posicao", position);
                engine.SetValue("jogadores", playerCount);
                var result = engine.Evaluate(ranking.ScoringFormula);
                var val = Convert.ToDecimal(result.ToObject());
                return Math.Max(0, Math.Round(val, 2));
            }
            catch { /* fallback */ }
        }

        // Fallback padrão
        return Math.Max(0, playerCount - position + 1);
    }

    public class PositionPoints
    {
        public int Position { get; set; }
        public decimal Points { get; set; }
    }
}
