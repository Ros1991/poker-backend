using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/home-games/{homeGameId:guid}/rankings")]
[Authorize]
public class HomeGameRankingsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;

    public HomeGameRankingsController(IAppDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
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

        _db.Rankings.Add(ranking);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/rankings/{ranking.Id}",
            new { ranking.Id, ranking.Name });
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

        // Busca todos os torneios do ranking que já finalizaram
        var tournaments = await _db.Tournaments
            .Where(t => t.RankingId == id && t.Status == "Finished")
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct);

        var tournamentIds = tournaments.Select(t => t.Id).ToList();
        if (tournamentIds.Count == 0)
            return Ok(new List<RankingLeaderboardResponse>());

        // Busca entries finalizadas desses torneios
        var entries = await _db.TournamentEntries
            .Include(e => e.Person)
            .Where(e => tournamentIds.Contains(e.TournamentId)
                     && e.FinalPosition != null
                     && (e.Status == "Eliminated" || e.Status == "Awarded"))
            .ToListAsync(ct);

        // Conta jogadores por torneio (para fórmula)
        var playerCounts = entries
            .GroupBy(e => e.TournamentId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Parse da tabela de pontuação se houver
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

        // Calcula pontos por entry
        var scores = entries.Select(e =>
        {
            var pos = e.FinalPosition ?? 0;
            var total = playerCounts.GetValueOrDefault(e.TournamentId, 0);
            decimal points = CalculatePoints(ranking, table, pos, total);
            return new
            {
                e.PersonId,
                Person = e.Person,
                e.TournamentId,
                Position = pos,
                Points = points
            };
        }).ToList();

        var discard = Math.Max(0, ranking.DiscardCount);
        var totalEtapas = tournamentIds.Count;
        var slotsToKeep = Math.Max(1, totalEtapas - discard);

        var leaderboard = scores
            .GroupBy(s => s.PersonId)
            .Select(g =>
            {
                var realPoints = g.OrderByDescending(s => s.Points).Select(s => s.Points).ToList();
                var absences = totalEtapas - realPoints.Count;
                for (int i = 0; i < absences; i++) realPoints.Add(0m);
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
        // Fórmula padrão: (playerCount - position + 1), mínimo 0
        return Math.Max(0, playerCount - position + 1);
    }

    public class PositionPoints
    {
        public int Position { get; set; }
        public decimal Points { get; set; }
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}
