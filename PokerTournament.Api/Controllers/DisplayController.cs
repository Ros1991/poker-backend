using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[AllowAnonymous]
public class DisplayController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly TimerService _timerService;

    public DisplayController(IAppDbContext db, TimerService timerService)
    {
        _db = db;
        _timerService = timerService;
    }

    [HttpGet("tournaments/{tournamentId:guid}")]
    public async Task<ActionResult<TvDisplayResponse>> GetTvDisplay(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament is null)
            return NotFound(new { message = "Torneio não encontrado." });

        // Blinds
        TimerStateResponse? blinds = null;
        try
        {
            blinds = await _timerService.GetTimerStateAsync(tournamentId, ct);
        }
        catch
        {
            blinds = new TimerStateResponse();
        }

        // Tables
        var tables = await _db.TournamentTables
            .Include(t => t.Entries.Where(e => e.Status == nameof(EntryStatus.Active)))
            .Where(t => t.TournamentId == tournamentId && t.IsActive)
            .OrderBy(t => t.TableNumber)
            .ToListAsync(ct);

        // Recent eliminations
        var eliminations = await _db.Eliminations
            .Include(e => e.Entry).ThenInclude(e => e.Person)
            .Include(e => e.EliminatedByEntry).ThenInclude(e => e!.Person)
            .Where(e => e.TournamentId == tournamentId && !e.Corrected)
            .OrderByDescending(e => e.EliminatedAt)
            .Take(10)
            .ToListAsync(ct);

        // Prizes
        var prizes = await _db.TournamentPrizes
            .Where(p => p.TournamentId == tournamentId)
            .OrderBy(p => p.Position)
            .ToListAsync(ct);

        // ITM players
        var itmPlayers = await _db.TournamentEntries
            .Include(e => e.Person)
            .Where(e => e.TournamentId == tournamentId
                     && e.FinalPosition.HasValue
                     && e.FinalPosition <= prizes.Count)
            .OrderBy(e => e.FinalPosition)
            .ToListAsync(ct);

        // Average stack
        var activePlayers = tournament.PlayersRemaining > 0 ? tournament.PlayersRemaining : 1;
        var totalChips = tournament.TotalEntries * tournament.StartingStack
            + tournament.TotalRebuys * (tournament.RebuyStack ?? 0)
            + tournament.TotalAddons * (tournament.AddonStack ?? 0);
        var averageStack = totalChips / activePlayers;

        return Ok(new TvDisplayResponse
        {
            Tournament = new TvTournamentInfo
            {
                Id = tournament.Id,
                Name = tournament.Name,
                Status = tournament.Status,
                Date = tournament.Date
            },
            Blinds = blinds,
            Stats = new TvStats
            {
                TotalEntries = tournament.TotalEntries,
                PlayersRemaining = tournament.PlayersRemaining,
                TotalRebuys = tournament.TotalRebuys,
                TotalAddons = tournament.TotalAddons,
                TotalPrizePool = tournament.TotalPrizePool,
                AverageStack = averageStack
            },
            Tables = tables.Select(t => new TvTableInfo
            {
                Id = t.Id,
                TableNumber = t.TableNumber,
                TableName = t.TableName,
                PlayerCount = t.Entries.Count,
                MaxSeats = t.MaxSeats
            }).ToList(),
            RecentEliminations = eliminations.Select(e => new TvEliminationInfo
            {
                PlayerName = e.Entry.Person.FullName,
                PlayerPhoto = e.Entry.Person.PhotoUrl,
                Position = e.Position ?? 0,
                EliminatedByName = e.EliminatedByEntry?.Person.FullName,
                EliminatedAt = e.EliminatedAt
            }).ToList(),
            Prizes = prizes.Select(p => new PrizeAllocation
            {
                Position = p.Position,
                Amount = p.Amount,
                Percentage = p.Percentage ?? 0
            }).ToList(),
            InTheMoney = itmPlayers.Select(e => new TvInTheMoneyInfo
            {
                PlayerName = e.Person.FullName,
                PlayerPhoto = e.Person.PhotoUrl,
                Position = e.FinalPosition ?? 0,
                PrizeAmount = e.PrizeAmount ?? 0
            }).ToList()
        });
    }
}
