using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

public class TimerService
{
    public const string LastLevelMessage = "Já está no último nível.";

    private readonly IAppDbContext _db;

    public TimerService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<TimerStateResponse> GetTimerStateAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var (tournament, levels) = await GetTournamentWithLevelsAsync(tournamentId, ct);

        bool changed = false;

        // SPEC-20261029-1720 (D2): torneio antigo com CurrentLevel além do fim (o "Próximo" não tinha trava e levava
        // a um nível que não existe — telão com 0/0 e o relógio voltando para 20:00). A leitura satura no último nível,
        // com o tempo dele já esgotado (relógio em 00:00), e grava a correção.
        var lastLevel = levels[^1];
        if ((tournament.CurrentLevel ?? 0) > lastLevel.LevelNumber)
        {
            tournament.CurrentLevel = lastLevel.LevelNumber;
            tournament.TimerElapsedSeconds = lastLevel.DurationMinutes * 60;
            tournament.TimerStartedAt = tournament.IsTimerRunning ? DateTimeOffset.UtcNow : null;
            changed = true;
        }

        // B4: auto-avanço de nível — consome o tempo decorrido através dos níveis e
        // persiste o avanço. O relógio é server-authoritative: qualquer leitura/poll
        // (dashboard, telão, terminal) reflete o nível correto sem clique manual.
        if (tournament.IsTimerRunning)
        {
            while (true)
            {
                var lvl = levels.FirstOrDefault(l => l.LevelNumber == tournament.CurrentLevel);
                int dur = (lvl?.DurationMinutes ?? 0) * 60;
                if (dur <= 0) break;
                int el = tournament.TimerElapsedSeconds
                    + (tournament.TimerStartedAt.HasValue
                        ? (int)(DateTimeOffset.UtcNow - tournament.TimerStartedAt.Value).TotalSeconds
                        : 0);
                if (el < dur) break;
                if (!levels.Any(l => l.LevelNumber == (tournament.CurrentLevel ?? 0) + 1))
                    break; // último nível: deixa saturar em 0, não avança além
                tournament.CurrentLevel = (tournament.CurrentLevel ?? 0) + 1;
                tournament.TimerElapsedSeconds = el - dur; // excedente conta para o próximo nível
                tournament.TimerStartedAt = DateTimeOffset.UtcNow;
                changed = true;
            }
        }

        if (changed)
            await _db.SaveChangesAsync(ct);

        var currentLevel = levels.FirstOrDefault(l => l.LevelNumber == tournament.CurrentLevel);
        var nextLevel = levels.FirstOrDefault(l => l.LevelNumber == (tournament.CurrentLevel ?? 0) + 1);

        int elapsedSeconds = tournament.TimerElapsedSeconds;

        if (tournament.IsTimerRunning && tournament.TimerStartedAt.HasValue)
        {
            elapsedSeconds += (int)(DateTimeOffset.UtcNow - tournament.TimerStartedAt.Value).TotalSeconds;
        }

        int totalSeconds = (currentLevel?.DurationMinutes ?? 20) * 60;
        int remainingSeconds = Math.Max(0, totalSeconds - elapsedSeconds);

        return new TimerStateResponse
        {
            CurrentLevel = tournament.CurrentLevel,
            SmallBlind = currentLevel?.SmallBlind ?? 0,
            BigBlind = currentLevel?.BigBlind ?? 0,
            Ante = currentLevel?.Ante ?? 0,
            DurationMinutes = currentLevel?.DurationMinutes ?? 0,
            ElapsedSeconds = elapsedSeconds,
            RemainingSeconds = remainingSeconds,
            IsRunning = tournament.IsTimerRunning,
            IsBreak = currentLevel?.IsBreak ?? false,
            // SPEC-20261029-1720: não há nível depois deste — "Próximo" desligado e, com o tempo zerado, o selo
            // "Último nível" no telão
            IsLastLevel = nextLevel is null,
            NextLevel = nextLevel is not null ? new NextLevelInfo
            {
                LevelNumber = nextLevel.LevelNumber,
                SmallBlind = nextLevel.SmallBlind,
                BigBlind = nextLevel.BigBlind,
                Ante = nextLevel.Ante,
                IsBreak = nextLevel.IsBreak
            } : null
        };
    }

    public async Task StartTimerAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var tournament = await GetTournamentAsync(tournamentId, ct);

        if (tournament.IsTimerRunning)
            throw new DomainException("Timer já está rodando.");

        tournament.IsTimerRunning = true;
        tournament.TimerStartedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task PauseTimerAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var tournament = await GetTournamentAsync(tournamentId, ct);

        if (!tournament.IsTimerRunning)
            throw new DomainException("Timer não está rodando.");

        if (tournament.TimerStartedAt.HasValue)
        {
            tournament.TimerElapsedSeconds += (int)(DateTimeOffset.UtcNow - tournament.TimerStartedAt.Value).TotalSeconds;
        }

        tournament.IsTimerRunning = false;
        tournament.TimerPausedAt = DateTimeOffset.UtcNow;
        tournament.TimerStartedAt = null;

        await _db.SaveChangesAsync(ct);
    }

    public async Task ResumeTimerAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var tournament = await GetTournamentAsync(tournamentId, ct);

        if (tournament.IsTimerRunning)
            throw new DomainException("Timer já está rodando.");

        tournament.IsTimerRunning = true;
        tournament.TimerStartedAt = DateTimeOffset.UtcNow;
        tournament.TimerPausedAt = null;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<TimerStateResponse> AdvanceLevelAsync(
        Guid tournamentId, Guid? userId = null, CancellationToken ct = default)
    {
        // SPEC-20261029-1720 (D2): sem nível depois do atual o "Próximo" é recusado (antes levava a um nível que não
        // existe e o telão "zerava"); para continuar, o organizador acrescenta níveis na aba Blinds
        var (tournament, levels) = await GetTournamentWithLevelsAsync(tournamentId, ct);

        if (!levels.Any(l => l.LevelNumber == (tournament.CurrentLevel ?? 0) + 1))
            throw new DomainException(LastLevelMessage);

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Tournament",
            EntityId = tournamentId,
            Action = "BlindLevelAdvance",
            OldValues = $"{{\"Level\":{tournament.CurrentLevel}}}",
            NewValues = $"{{\"Level\":{(tournament.CurrentLevel ?? 0) + 1}}}",
            UserId = userId,
            TournamentId = tournamentId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        tournament.CurrentLevel = (tournament.CurrentLevel ?? 0) + 1;
        tournament.TimerElapsedSeconds = 0;
        tournament.TimerStartedAt = tournament.IsTimerRunning ? DateTimeOffset.UtcNow : null;

        await _db.SaveChangesAsync(ct);
        return await GetTimerStateAsync(tournamentId, ct);
    }

    public async Task<TimerStateResponse> PreviousLevelAsync(
        Guid tournamentId, Guid? userId = null, CancellationToken ct = default)
    {
        var tournament = await GetTournamentAsync(tournamentId, ct);

        if ((tournament.CurrentLevel ?? 1) <= 1)
            throw new DomainException("Já está no primeiro nível.");

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Tournament",
            EntityId = tournamentId,
            Action = "BlindLevelReverse",
            OldValues = $"{{\"Level\":{tournament.CurrentLevel}}}",
            NewValues = $"{{\"Level\":{tournament.CurrentLevel - 1}}}",
            UserId = userId,
            TournamentId = tournamentId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        tournament.CurrentLevel--;
        tournament.TimerElapsedSeconds = 0;
        tournament.TimerStartedAt = tournament.IsTimerRunning ? DateTimeOffset.UtcNow : null;

        await _db.SaveChangesAsync(ct);
        return await GetTimerStateAsync(tournamentId, ct);
    }

    private async Task<Tournament> GetTournamentAsync(Guid tournamentId, CancellationToken ct)
    {
        return await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");
    }

    /// <summary>
    /// Torneio (rastreado) e os níveis em ordem: os do próprio torneio; sem eles, os do template (dados antigos).
    /// Sem nenhum nível → <see cref="DomainException"/> "Torneio não possui estrutura de blinds configurada.".
    /// </summary>
    private async Task<(Tournament Tournament, List<BlindLevelView> Levels)> GetTournamentWithLevelsAsync(
        Guid tournamentId, CancellationToken ct)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.BlindLevels)
            .Include(t => t.BlindStructure)
                .ThenInclude(bs => bs!.Levels.OrderBy(l => l.LevelNumber))
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        // Prefer tournament-specific blind levels; fall back to template for legacy data
        var levels = tournament.BlindLevels.Any()
            ? tournament.BlindLevels
                .OrderBy(l => l.LevelNumber)
                .Select(l => new BlindLevelView
                {
                    LevelNumber = l.LevelNumber,
                    SmallBlind = l.SmallBlind,
                    BigBlind = l.BigBlind,
                    Ante = l.Ante,
                    DurationMinutes = l.DurationMinutes,
                    IsBreak = l.IsBreak,
                })
                .ToList()
            : tournament.BlindStructure?.Levels
                .OrderBy(l => l.LevelNumber)
                .Select(l => new BlindLevelView
                {
                    LevelNumber = l.LevelNumber,
                    SmallBlind = l.SmallBlind,
                    BigBlind = l.BigBlind,
                    Ante = l.Ante,
                    DurationMinutes = l.DurationMinutes,
                    IsBreak = l.IsBreak,
                })
                .ToList();

        if (levels is null || levels.Count == 0)
            throw new DomainException("Torneio não possui estrutura de blinds configurada.");

        return (tournament, levels);
    }

    private class BlindLevelView
    {
        public int LevelNumber { get; set; }
        public int SmallBlind { get; set; }
        public int BigBlind { get; set; }
        public int Ante { get; set; }
        public int DurationMinutes { get; set; }
        public bool IsBreak { get; set; }
    }
}
