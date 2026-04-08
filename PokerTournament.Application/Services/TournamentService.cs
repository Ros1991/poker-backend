using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

public class TournamentService
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;

    public TournamentService(IAppDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public async Task<PaginatedResponse<TournamentResponse>> GetAllAsync(
        Guid homeGameId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = _db.Tournaments
            .Where(t => t.HomeGameId == homeGameId && t.Status != nameof(TournamentStatus.Cancelled))
            .OrderByDescending(t => t.Date);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var mapped = _mapper.Map<List<TournamentResponse>>(items);
        return new PaginatedResponse<TournamentResponse>(mapped, page, pageSize, totalCount);
    }

    public async Task<TournamentResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        return _mapper.Map<TournamentResponse>(tournament);
    }

    public async Task<TournamentResponse> CreateAsync(
        Guid homeGameId, CreateTournamentRequest request, Guid? createdBy = null, CancellationToken ct = default)
    {
        var homeGame = await _db.HomeGames
            .FirstOrDefaultAsync(h => h.Id == homeGameId, ct)
            ?? throw new DomainException("Home game não encontrado.");

        var tournament = _mapper.Map<Tournament>(request);
        tournament.HomeGameId = homeGameId;
        tournament.Status = nameof(TournamentStatus.Draft);
        tournament.CreatedBy = createdBy;
        tournament.CurrentLevel = 1;

        // Copy blind levels from template if specified
        if (request.BlindStructureId.HasValue)
        {
            var structure = await _db.BlindStructures
                .Include(bs => bs.Levels)
                .FirstOrDefaultAsync(bs => bs.Id == request.BlindStructureId.Value, ct);
            if (structure != null)
            {
                foreach (var level in structure.Levels.OrderBy(l => l.LevelNumber))
                {
                    tournament.BlindLevels.Add(new TournamentBlindLevel
                    {
                        LevelNumber = level.LevelNumber,
                        SmallBlind = level.SmallBlind,
                        BigBlind = level.BigBlind,
                        Ante = level.Ante,
                        BigBlindAnte = level.BigBlindAnte,
                        DurationMinutes = level.DurationMinutes,
                        IsBreak = level.IsBreak,
                        BreakDescription = level.BreakDescription,
                    });
                }
            }
        }

        _db.Tournaments.Add(tournament);
        await _db.SaveChangesAsync(ct);

        return _mapper.Map<TournamentResponse>(tournament);
    }

    public async Task<TournamentResponse> UpdateAsync(
        Guid id, CreateTournamentRequest request, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.BlindLevels)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (tournament.Status != nameof(TournamentStatus.Draft))
            throw new DomainException("Apenas torneios em rascunho podem ser editados.");

        var previousStructureId = tournament.BlindStructureId;
        _mapper.Map(request, tournament);

        // If the blind structure changed (or was set when previously null), copy levels
        if (request.BlindStructureId.HasValue && request.BlindStructureId != previousStructureId)
        {
            foreach (var existing in tournament.BlindLevels.ToList())
                _db.TournamentBlindLevels.Remove(existing);

            var structure = await _db.BlindStructures
                .Include(bs => bs.Levels)
                .FirstOrDefaultAsync(bs => bs.Id == request.BlindStructureId.Value, ct);
            if (structure != null)
            {
                foreach (var level in structure.Levels.OrderBy(l => l.LevelNumber))
                {
                    tournament.BlindLevels.Add(new TournamentBlindLevel
                    {
                        LevelNumber = level.LevelNumber,
                        SmallBlind = level.SmallBlind,
                        BigBlind = level.BigBlind,
                        Ante = level.Ante,
                        BigBlindAnte = level.BigBlindAnte,
                        DurationMinutes = level.DurationMinutes,
                        IsBreak = level.IsBreak,
                        BreakDescription = level.BreakDescription,
                    });
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        return _mapper.Map<TournamentResponse>(tournament);
    }

    public async Task<List<TournamentBlindLevelResponse>> GetBlindLevelsAsync(
        Guid tournamentId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.BlindLevels)
            .Include(t => t.BlindStructure)
                .ThenInclude(bs => bs!.Levels)
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        // If tournament has its own levels, use them
        if (tournament.BlindLevels.Any())
        {
            return tournament.BlindLevels
                .OrderBy(l => l.LevelNumber)
                .Select(l => _mapper.Map<TournamentBlindLevelResponse>(l))
                .ToList();
        }

        // Fallback: map from the template (legacy data)
        if (tournament.BlindStructure != null)
        {
            return tournament.BlindStructure.Levels
                .OrderBy(l => l.LevelNumber)
                .Select(l => new TournamentBlindLevelResponse
                {
                    Id = l.Id,
                    LevelNumber = l.LevelNumber,
                    SmallBlind = l.SmallBlind,
                    BigBlind = l.BigBlind,
                    Ante = l.Ante,
                    BigBlindAnte = l.BigBlindAnte,
                    DurationMinutes = l.DurationMinutes,
                    IsBreak = l.IsBreak,
                    BreakDescription = l.BreakDescription,
                })
                .ToList();
        }

        return [];
    }

    public async Task UpdateBlindLevelsAsync(
        Guid homeGameId, Guid tournamentId, UpdateTournamentBlindLevelsRequest request, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.BlindLevels)
            .FirstOrDefaultAsync(t => t.Id == tournamentId && t.HomeGameId == homeGameId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        foreach (var level in tournament.BlindLevels.ToList())
            _db.TournamentBlindLevels.Remove(level);

        foreach (var l in request.Levels.OrderBy(x => x.LevelNumber))
        {
            tournament.BlindLevels.Add(new TournamentBlindLevel
            {
                LevelNumber = l.LevelNumber,
                SmallBlind = l.SmallBlind,
                BigBlind = l.BigBlind,
                Ante = l.Ante,
                BigBlindAnte = l.BigBlindAnte,
                DurationMinutes = l.DurationMinutes,
                IsBreak = l.IsBreak,
                BreakDescription = l.BreakDescription,
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<TournamentResponse> UpdateStatusAsync(
        Guid id, string newStatus, Guid? userId = null, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (!Enum.TryParse<TournamentStatus>(newStatus, out var status))
            throw new DomainException($"Status inválido: {newStatus}");

        ValidateStatusTransition(tournament.Status, newStatus);

        var oldStatus = tournament.Status;
        tournament.Status = newStatus;

        // Ações específicas por transição de status
        switch (status)
        {
            case TournamentStatus.InProgress:
                tournament.TimerStartedAt = DateTimeOffset.UtcNow;
                tournament.IsTimerRunning = true;
                break;

            case TournamentStatus.Finished:
                tournament.IsTimerRunning = false;
                break;

            case TournamentStatus.Cancelled:
                tournament.IsTimerRunning = false;
                tournament.IsActive = false;
                break;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Tournament",
            EntityId = tournament.Id,
            Action = "StatusChange",
            OldValues = $"{{\"Status\":\"{oldStatus}\"}}",
            NewValues = $"{{\"Status\":\"{newStatus}\"}}",
            UserId = userId,
            TournamentId = tournament.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return _mapper.Map<TournamentResponse>(tournament);
    }

    private static void ValidateStatusTransition(string currentStatus, string newStatus)
    {
        var validTransitions = new Dictionary<string, string[]>
        {
            [nameof(TournamentStatus.Draft)] = [nameof(TournamentStatus.OpenForRegistration), nameof(TournamentStatus.InProgress), nameof(TournamentStatus.Cancelled)],
            [nameof(TournamentStatus.OpenForRegistration)] = [nameof(TournamentStatus.InProgress), nameof(TournamentStatus.Cancelled)],
            [nameof(TournamentStatus.InProgress)] = [nameof(TournamentStatus.BreakSettlement), nameof(TournamentStatus.Finished), nameof(TournamentStatus.Cancelled)],
            [nameof(TournamentStatus.BreakSettlement)] = [nameof(TournamentStatus.InProgress), nameof(TournamentStatus.Finished)],
            [nameof(TournamentStatus.Finished)] = [],
            [nameof(TournamentStatus.Cancelled)] = []
        };

        if (!validTransitions.TryGetValue(currentStatus, out var allowed) || !allowed.Contains(newStatus))
            throw new DomainException($"Transição de status inválida: {currentStatus} → {newStatus}");
    }
}
