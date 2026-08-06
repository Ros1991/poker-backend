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
            .Include(t => t.HomeGame)
            .Include(t => t.Ranking)
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
            .Include(t => t.HomeGame)
            .Include(t => t.Ranking)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        return _mapper.Map<TournamentResponse>(tournament);
    }

    /// <summary>
    /// Atualiza o bônus de pontualidade (nº de jogadores × fichas). Editável em
    /// qualquer status exceto Cancelled — é um ajuste operacional, não config de Draft.
    /// </summary>
    public async Task<TournamentResponse> UpdatePunctualityBonusAsync(
        Guid id, int count, int chips, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.HomeGame)
            .Include(t => t.Ranking)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (tournament.Status == nameof(TournamentStatus.Cancelled))
            throw new DomainException("Torneio cancelado não pode ser alterado.");

        if (count < 0 || chips < 0)
            throw new DomainException("Valores do bônus não podem ser negativos.");

        tournament.PunctualityBonusCount = count;
        tournament.PunctualityBonusChips = chips;
        await _db.SaveChangesAsync(ct);

        return _mapper.Map<TournamentResponse>(tournament);
    }

    /// <summary>
    /// Atualiza a chave PIX do dia. Editável em qualquer status exceto Cancelled —
    /// ajuste operacional (a chave pode mudar durante o torneio), não config de Draft.
    /// </summary>
    public async Task<TournamentResponse> UpdatePixKeyAsync(
        Guid id, string? pixKey, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.HomeGame)
            .Include(t => t.Ranking)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (tournament.Status == nameof(TournamentStatus.Cancelled))
            throw new DomainException("Torneio cancelado não pode ser alterado.");

        tournament.ResponsiblePixKey = string.IsNullOrWhiteSpace(pixKey) ? null : pixKey.Trim();
        await _db.SaveChangesAsync(ct);

        return _mapper.Map<TournamentResponse>(tournament);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        // Soft-delete mesmo se finalizado: o filtro global (DeletedAt == null) já
        // remove o torneio de QUALQUER query, inclusive os leaderboards de ranking.
        _db.Tournaments.Remove(tournament);
        await _db.SaveChangesAsync(ct);
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

        // Feature staff/ranking: cria os custos automáticos (Amount inicia em 0; cresce por entrada)
        if (tournament.StaffAmount is > 0)
        {
            tournament.CostExtras.Add(new CostExtra
            {
                Description = "Staff (casa)",
                Amount = 0,
                CostType = "Staff",
                Beneficiary = homeGame.PixBeneficiary ?? homeGame.Name,
                PixKey = homeGame.PixKey,
                PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending),
            });
        }
        if (tournament.RankingId.HasValue && tournament.RankingContribMode is not null
            && tournament.RankingContribValue is > 0)
        {
            tournament.CostExtras.Add(new CostExtra
            {
                Description = "Acumulado do ranking",
                Amount = 0,
                CostType = "RankingAccumulated",
                PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending),
            });
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

        // Garante custos automáticos caso staff/ranking tenham sido definidos só na edição.
        await EnsureAutoCostsAsync(tournament, ct);

        await _db.SaveChangesAsync(ct);

        return _mapper.Map<TournamentResponse>(tournament);
    }

    /// <summary>Cria (idempotente) os custos automáticos de staff/ranking se faltarem.</summary>
    private async Task EnsureAutoCostsAsync(Tournament tournament, CancellationToken ct)
    {
        var existing = await _db.CostExtras
            .Where(c => c.TournamentId == tournament.Id)
            .ToListAsync(ct);

        if (tournament.StaffAmount is > 0 && existing.All(c => c.CostType != "Staff"))
        {
            var homeGame = await _db.HomeGames
                .FirstOrDefaultAsync(h => h.Id == tournament.HomeGameId, ct);
            _db.CostExtras.Add(new CostExtra
            {
                TournamentId = tournament.Id,
                Description = "Staff (casa)",
                Amount = 0,
                CostType = "Staff",
                Beneficiary = homeGame?.PixBeneficiary ?? homeGame?.Name,
                PixKey = homeGame?.PixKey,
                PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending),
            });
        }

        if (tournament.RankingId.HasValue && tournament.RankingContribMode is not null
            && tournament.RankingContribValue is > 0
            && existing.All(c => c.CostType != "RankingAccumulated"))
        {
            _db.CostExtras.Add(new CostExtra
            {
                TournamentId = tournament.Id,
                Description = "Acumulado do ranking",
                Amount = 0,
                CostType = "RankingAccumulated",
                PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending),
            });
        }
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
                // Feature ranking: soma o acumulado final no Ranking.AccumulatedPrize (idempotente via snapshot)
                if (tournament.RankingId.HasValue && tournament.RankingPrizeAccrued is null)
                {
                    var rankingCost = await _db.CostExtras.FirstOrDefaultAsync(
                        c => c.TournamentId == tournament.Id && c.CostType == "RankingAccumulated", ct);
                    var accrued = rankingCost?.Amount ?? 0m;
                    if (accrued > 0)
                    {
                        var ranking = await _db.Rankings.FirstOrDefaultAsync(
                            r => r.Id == tournament.RankingId.Value, ct);
                        if (ranking is not null)
                            ranking.AccumulatedPrize += accrued;
                    }
                    tournament.RankingPrizeAccrued = accrued;
                    tournament.RankingAccruedAt = DateTimeOffset.UtcNow;
                }
                break;

            case TournamentStatus.Cancelled:
                tournament.IsTimerRunning = false;
                tournament.IsActive = false;
                // Se estava finalizado e já tinha acumulado no ranking, reverte (cancelar desfaz o aporte).
                if (tournament.RankingId.HasValue && tournament.RankingPrizeAccrued is > 0)
                {
                    var ranking = await _db.Rankings.FirstOrDefaultAsync(
                        r => r.Id == tournament.RankingId.Value, ct);
                    if (ranking is not null)
                        ranking.AccumulatedPrize = Math.Max(0m, ranking.AccumulatedPrize - tournament.RankingPrizeAccrued.Value);
                    tournament.RankingPrizeAccrued = null;
                    tournament.RankingAccruedAt = null;
                }
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
            // Finalizado pode ser cancelado (reverte o aporte no ranking — ver case Cancelled).
            [nameof(TournamentStatus.Finished)] = [nameof(TournamentStatus.Cancelled)],
            [nameof(TournamentStatus.Cancelled)] = []
        };

        if (!validTransitions.TryGetValue(currentStatus, out var allowed) || !allowed.Contains(newStatus))
            throw new DomainException($"Transição de status inválida: {currentStatus} → {newStatus}");
    }
}
