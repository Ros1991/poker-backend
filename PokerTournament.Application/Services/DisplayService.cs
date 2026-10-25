using System.Linq.Expressions;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

public enum SnapshotOutcome { InvalidDevice, NotPaired, Ok }

/// <summary>
/// TV pareada por QR (estilo Netflix). A TV não tem login: guarda um token de
/// aparelho e mostra um código curto; o celular logado "reivindica" o código
/// escolhendo o torneio. O telão lê um snapshot único, só de leitura.
///
/// A TV consulta o status a cada ~2s enquanto o celular pode estar pareando: por
/// isso as escritas são ExecuteUpdate atômicos, sem disputa de xmin.
/// </summary>
public class DisplayService
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CodeRenewMargin = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SeenInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(1);
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan SnapshotCacheTtl = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FinishedVisibleFor = TimeSpan.FromDays(2);
    private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ"; // sem 0/O/1/I/L
    private const int CodeLength = 6;

    private static readonly string Pending = nameof(DisplaySessionStatus.Pending);
    private static readonly string Paired = nameof(DisplaySessionStatus.Paired);
    private static readonly string Cancelled = nameof(TournamentStatus.Cancelled);
    private static readonly string Finished = nameof(TournamentStatus.Finished);

    private readonly IAppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly TimerService _timer;
    private readonly PrizeService _prizes;
    private readonly TournamentService _tournaments;
    private readonly PlanService _plans;

    public DisplayService(
        IAppDbContext db, IMemoryCache cache, TimerService timer, PrizeService prizes, TournamentService tournaments,
        PlanService plans)
    {
        _db = db;
        _cache = cache;
        _timer = timer;
        _prizes = prizes;
        _tournaments = tournaments;
        _plans = plans;
    }

    // ---------------------------------------------------------------- TV (sem login)

    public async Task<DisplaySessionCreatedResponse> CreateAsync(string? userAgent, CancellationToken ct = default)
    {
        await CleanupAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var deviceToken = NewDeviceToken();
        var session = new DisplaySession
        {
            DeviceTokenHash = AuthService.HashToken(deviceToken),
            Status = Pending,
            PairingCode = await NewUniqueCodeAsync(ct),
            CodeExpiresAt = now + CodeLifetime,
            LastSeenAt = now,
            UserAgent = Truncate(userAgent),
        };
        _db.DisplaySessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return new DisplaySessionCreatedResponse
        {
            DeviceToken = deviceToken,
            Session = await ToStatusAsync(session, ct),
        };
    }

    /// <summary>Status da TV; mantém o código sempre válido. Null = token inválido.</summary>
    public async Task<DisplaySessionStatusResponse?> GetStatusAsync(string? deviceToken, CancellationToken ct = default)
    {
        var session = await FindByTokenAsync(deviceToken, ct);
        if (session is null)
            return null;

        var now = DateTimeOffset.UtcNow;

        // Torneio excluído ou cancelado: a TV volta para o QR grande
        if (session.Status == Paired && !await IsShowableAsync(session.TournamentId, ct))
            await UnpairAsync(session.Id, ct);

        if (session.PairingCode is null || session.CodeExpiresAt is null || session.CodeExpiresAt - now < CodeRenewMargin)
            await RenewCodeAsync(session.Id, ct);

        if (now - session.LastSeenAt > SeenInterval)
            await _db.DisplaySessions.Where(s => s.Id == session.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastSeenAt, now), ct);

        var fresh = await _db.DisplaySessions.AsNoTracking().FirstAsync(s => s.Id == session.Id, ct);
        return await ToStatusAsync(fresh, ct);
    }

    /// <summary>Botão "Voltar" do telão: sai do torneio e mostra o QR grande.</summary>
    public async Task<DisplaySessionStatusResponse?> LeaveAsync(string? deviceToken, CancellationToken ct = default)
    {
        var session = await FindByTokenAsync(deviceToken, ct);
        if (session is null)
            return null;

        await UnpairAsync(session.Id, ct);
        return await GetStatusAsync(deviceToken, ct);
    }

    public async Task<(SnapshotOutcome Outcome, TvSnapshotResponse? Snapshot)> GetSnapshotAsync(
        string? deviceToken, CancellationToken ct = default)
    {
        var session = await FindByTokenAsync(deviceToken, ct);
        if (session is null)
            return (SnapshotOutcome.InvalidDevice, null);
        if (session.Status != Paired || session.TournamentId is null)
            return (SnapshotOutcome.NotPaired, null);

        var tournamentId = session.TournamentId.Value;
        var snapshot = await _cache.GetOrCreateAsync($"tv-snapshot:{tournamentId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SnapshotCacheTtl;
            return await BuildSnapshotAsync(tournamentId, ct);
        });

        // Torneio sumiu: o próximo status desfaz o pareamento
        return snapshot is null
            ? (SnapshotOutcome.NotPaired, null)
            : (SnapshotOutcome.Ok, WithFreshTimer(snapshot));
    }

    // ------------------------------------------------------------ celular (logado)

    /// <summary>
    /// Confere o código lido. O nome do torneio que a TV já mostra só aparece para
    /// quem pode trocá-lo (mesma regra do claim).
    /// </summary>
    public async Task<PairingCodeLookupResponse> LookupCodeAsync(
        string? code, Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        var normalized = NormalizeCode(code);
        if (normalized is null)
            return new PairingCodeLookupResponse { Valid = false };

        var now = DateTimeOffset.UtcNow;
        var session = await _db.DisplaySessions.AsNoTracking()
            .FirstOrDefaultAsync(HasValidCode(normalized, now), ct);
        if (session is null)
            return new PairingCodeLookupResponse { Valid = false };

        string? current = null;
        if (session.Status == Paired && session.TournamentId is not null
            && await CanReplaceAsync(session, userId, isAdmin, ct))
            current = await _db.Tournaments.Where(t => t.Id == session.TournamentId)
                .Select(t => t.Name).FirstOrDefaultAsync(ct);

        return new PairingCodeLookupResponse { Valid = true, CurrentTournamentName = current };
    }

    /// <summary>Celular escolhe o torneio para a TV daquele código (também troca de torneio).</summary>
    public async Task<DisplayPairedResponse> ClaimAsync(
        string? code, Guid tournamentId, Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        var normalized = NormalizeCode(code)
            ?? throw new DomainException("Código inválido. Confira o código mostrado na TV.");

        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        if (tournament.Status == Cancelled)
            throw new DomainException("Esse torneio foi cancelado.");

        var now = DateTimeOffset.UtcNow;
        var session = await _db.DisplaySessions.AsNoTracking()
            .FirstOrDefaultAsync(HasValidCode(normalized, now), ct)
            ?? throw new DomainException("Código inválido ou expirado. Confira o código mostrado na TV.");

        // O código fica visível na TV pareada: sem esta regra, qualquer conta trocaria
        // o telão de outro grupo para um torneio dela
        if (!await CanReplaceAsync(session, userId, isAdmin, ct))
            throw new DomainException("Esta TV está mostrando um torneio de outro grupo. Toque em Voltar na TV para liberar.");

        // Atômico: o código lido (atual e anterior) deixa de valer na hora (a TV recebe
        // outro no próximo status). Só grava se a TV não mudou desde a conferência acima.
        var currentStatus = session.Status;
        var currentTournament = session.TournamentId;
        var affected = await _db.DisplaySessions
            .Where(s => s.Id == session.Id && s.Status == currentStatus && s.TournamentId == currentTournament)
            .Where(HasValidCode(normalized, now))
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Status, Paired)
                .SetProperty(s => s.TournamentId, tournamentId)
                .SetProperty(s => s.PairedByUserId, userId)
                .SetProperty(s => s.PairedAt, now)
                .SetProperty(s => s.PairingCode, (string?)null)
                .SetProperty(s => s.CodeExpiresAt, (DateTimeOffset?)null)
                .SetProperty(s => s.PreviousPairingCode, (string?)null)
                .SetProperty(s => s.PreviousCodeExpiresAt, (DateTimeOffset?)null)
                .SetProperty(s => s.UpdatedAt, now), ct);

        if (affected == 0)
            throw new DomainException("Código inválido ou expirado. Confira o código mostrado na TV.");

        return new DisplayPairedResponse { TournamentId = tournamentId, TournamentName = tournament.Name };
    }

    /// <summary>Botão "TV Display" do painel: este navegador logado vira a TV do torneio.</summary>
    public async Task<DisplayDeviceTokenResponse> PairDirectAsync(
        Guid tournamentId, Guid userId, string? userAgent, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        if (tournament.Status == Cancelled)
            throw new DomainException("Esse torneio foi cancelado.");

        var now = DateTimeOffset.UtcNow;
        var deviceToken = NewDeviceToken();
        _db.DisplaySessions.Add(new DisplaySession
        {
            DeviceTokenHash = AuthService.HashToken(deviceToken),
            Status = Paired,
            TournamentId = tournamentId,
            PairedByUserId = userId,
            PairedAt = now,
            PairingCode = await NewUniqueCodeAsync(ct),
            CodeExpiresAt = now + CodeLifetime,
            LastSeenAt = now,
            UserAgent = Truncate(userAgent),
        });
        await _db.SaveChangesAsync(ct);

        return new DisplayDeviceTokenResponse { DeviceToken = deviceToken };
    }

    /// <summary>Torneios que o usuário pode pôr na TV: em andamento primeiro.</summary>
    public async Task<List<PairableTournamentResponse>> GetPairableTournamentsAsync(
        Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        var finishedSince = DateTimeOffset.UtcNow - FinishedVisibleFor;
        var query = _db.Tournaments.AsNoTracking()
            .Where(t => t.Status != Cancelled && (t.Status != Finished || t.Date >= finishedSince));

        if (!isAdmin)
        {
            var homeGameIds = await HomeGameAccess.AccessibleHomeGameIdsAsync(_db, userId, ct);
            query = query.Where(t => homeGameIds.Contains(t.HomeGameId));
        }

        var list = await query
            .Join(_db.HomeGames, t => t.HomeGameId, h => h.Id, (t, h) => new PairableTournamentResponse
            {
                Id = t.Id,
                Name = t.Name,
                Date = t.Date,
                Status = t.Status,
                HomeGameName = h.Name,
            })
            .OrderByDescending(t => t.Date)
            .Take(40)
            .ToListAsync(ct);

        return list.OrderBy(t => StatusRank(t.Status)).ThenByDescending(t => t.Date).ToList();
    }

    // ---------------------------------------------------------------- snapshot

    private async Task<TvSnapshotResponse?> BuildSnapshotAsync(Guid tournamentId, CancellationToken ct)
    {
        var t = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tournamentId, ct);
        if (t is null)
            return null;

        // SPEC-20261023-1205: o telão segue o plano efetivo do DONO do home game. No Mesa (D13/D3/D4)
        // ele é só vitrine — relógio, jogadores e eliminados: sem prêmios, sem mesas, sem PIX e sem
        // saldos (a coluna "Deve" some). O backend é a fonte; o frontend só não mostra o que não veio.
        var homeGame = await _db.HomeGames.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.Id == t.HomeGameId)
            .Select(h => new { h.OwnerId, h.LogoUrl })
            .FirstOrDefaultAsync(ct);
        var plan = homeGame is null ? PlanCatalog.Mesa : await _plans.GetUserEffectivePlanAsync(homeGame.OwnerId, ct);
        var planDefinition = PlanCatalog.Get(plan);
        var showPrizes = _plans.Allows(plan, PlanFeature.Prizes);
        var showTables = _plans.Allows(plan, PlanFeature.Tables);
        var showMoney = _plans.Allows(plan, PlanFeature.Payments);
        var showPix = _plans.Allows(plan, PlanFeature.PixCardOnTv);

        TimerStateResponse? timer = null;
        try { timer = await _timer.GetTimerStateAsync(tournamentId, ct); }
        catch (DomainException) { /* torneio sem estrutura de blinds */ }

        var levels = (await _tournaments.GetBlindLevelsAsync(tournamentId, ct))
            .Select(l => new TvBlindLevel { LevelNumber = l.LevelNumber, DurationMinutes = l.DurationMinutes, IsBreak = l.IsBreak })
            .ToList();

        // IgnoreQueryFilters: e.Person na projeção é INNER JOIN com a pessoa filtrada — o jogador removido do cadastro
        // sumiria do telão (contagem e lugar); ele continua no torneio com o nome (EntryQueries)
        var entries = await _db.TournamentEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TournamentId == tournamentId)
            .OrderBy(e => e.RegisteredAt)
            .Select(e => new TvEntry
            {
                Id = e.Id,
                PersonId = e.PersonId,
                DisplayName = e.Person.Nickname ?? e.Person.FullName,
                Status = e.Status,
                FinalPosition = e.FinalPosition,
                TableId = e.TableId,
                SeatNumber = e.SeatNumber,
                RebuyCount = e.RebuyCount,
                AddonPurchased = e.AddonPurchased,
                AddonDouble = e.AddonDouble,
                Balance = e.Balance,
                TotalDue = e.TotalDue,
                PrizeAmount = e.PrizeAmount,
            })
            .ToListAsync(ct);

        // Prize pool sem premiação no plano: calculado antes de zerar os saldos (não vai ao telão)
        var collected = entries.Sum(e => e.TotalDue);
        foreach (var entry in entries)
        {
            if (!showMoney)
            {
                entry.Balance = 0;
                entry.TotalDue = 0;
            }
            if (!showPrizes)
                entry.PrizeAmount = null;
            if (!showTables)
            {
                entry.TableId = null;
                entry.SeatNumber = null;
            }
        }

        var activeStatus = nameof(EntryStatus.Active);
        var tables = !showTables ? new List<TvTable>() : await _db.TournamentTables.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TournamentId == tournamentId && x.IsActive)
            .OrderBy(x => x.TableNumber)
            .Select(x => new TvTable
            {
                Id = x.Id,
                TableNumber = x.TableNumber,
                TableName = x.TableName,
                MaxSeats = x.MaxSeats,
                DealerName = x.DealerPerson != null ? (x.DealerPerson.Nickname ?? x.DealerPerson.FullName) : null,
                Players = x.Entries
                    .Where(en => en.Status == activeStatus)
                    .OrderBy(en => en.SeatNumber)
                    .Select(en => new TvSeat
                    {
                        SeatNumber = en.SeatNumber,
                        PersonId = en.PersonId,
                        Name = en.Person.Nickname ?? en.Person.FullName,
                    })
                    .ToList(),
            })
            .ToListAsync(ct);

        List<TvPrize> prizes;
        decimal prizePool;
        if (!showPrizes)
        {
            prizes = [];
            prizePool = 0;
        }
        else if (t.PrizeConfirmed)
        {
            prizes = await _db.TournamentPrizes.AsNoTracking()
                .Where(p => p.TournamentId == tournamentId)
                .OrderBy(p => p.Position)
                .Select(p => new TvPrize { Position = p.Position, Amount = p.Amount })
                .ToListAsync(ct);
            prizePool = prizes.Sum(p => p.Amount);
        }
        else
        {
            try
            {
                prizes = (await _prizes.CalculatePrizesAsync(tournamentId, null, ct)).Prizes
                    .Select(p => new TvPrize { Position = p.Position, Amount = p.Amount })
                    .ToList();
            }
            catch (DomainException)
            {
                prizes = []; // prêmio ainda não calculável (poucos jogadores / pool zerado)
            }
            var costs = await _db.CostExtras.Where(c => c.TournamentId == tournamentId).SumAsync(c => c.Amount, ct);
            // Telão nunca mostra prêmio negativo (SPEC-20261023-1215, CP10): com custo fixo (ex.: churrasqueiro)
            // o líquido começa negativo até a receita cobrir os custos. Financeiro e Premiação mostram o valor real.
            prizePool = Math.Max(0m, collected - costs);
        }

        return new TvSnapshotResponse
        {
            Tournament = new TvTournamentInfo
            {
                Id = t.Id,
                Name = t.Name,
                Status = t.Status,
                PrizeConfirmed = t.PrizeConfirmed,
                StartingStack = t.StartingStack,
                RebuyStack = t.RebuyStack,
                AddonStack = t.AddonStack,
                PunctualityBonusCount = t.PunctualityBonusCount,
                PunctualityBonusChips = t.PunctualityBonusChips,
                TotalEntries = t.TotalEntries,
                TotalRebuys = t.TotalRebuys,
                TotalAddons = t.TotalAddons,
                ResponsiblePixKey = showPix ? t.ResponsiblePixKey : null,
                Plan = plan,
                ShowBadge = planDefinition.ShowBadge,
                // O telão decide o conteúdo por aqui (com a chave de emergência desligada, todos os recursos)
                PlanFeatures = [.. _plans.FeaturesFor(plan)],
                // Decoração segue o plano de verdade (não a chave de emergência): logo só na Liga
                ClubLogoUrl = planDefinition.Has(PlanFeature.ClubLogoOnTv) ? homeGame?.LogoUrl : null,
            },
            Timer = timer,
            BlindLevels = levels,
            Entries = entries,
            Tables = tables,
            Prizes = prizes,
            PrizePool = prizePool,
            GeneratedAt = DateTimeOffset.UtcNow,
        };
    }

    // O relógio da TV recalibra pelo servidor: desconta o tempo que o snapshot
    // ficou no cache para não "voltar" 1-2s a cada atualização. Nunca altera o cacheado.
    private static TvSnapshotResponse WithFreshTimer(TvSnapshotResponse cached)
    {
        if (cached.Timer is not { IsRunning: true } timer)
            return cached;

        var elapsed = (int)Math.Floor((DateTimeOffset.UtcNow - cached.GeneratedAt).TotalSeconds);
        if (elapsed <= 0)
            return cached;

        var adjusted = new TimerStateResponse
        {
            CurrentLevel = timer.CurrentLevel,
            SmallBlind = timer.SmallBlind,
            BigBlind = timer.BigBlind,
            Ante = timer.Ante,
            DurationMinutes = timer.DurationMinutes,
            ElapsedSeconds = timer.ElapsedSeconds + elapsed,
            RemainingSeconds = Math.Max(0, timer.RemainingSeconds - elapsed),
            IsRunning = timer.IsRunning,
            IsBreak = timer.IsBreak,
            NextLevel = timer.NextLevel,
        };

        return new TvSnapshotResponse
        {
            Tournament = cached.Tournament,
            Timer = adjusted,
            BlindLevels = cached.BlindLevels,
            Entries = cached.Entries,
            Tables = cached.Tables,
            Prizes = cached.Prizes,
            PrizePool = cached.PrizePool,
            GeneratedAt = DateTimeOffset.UtcNow,
        };
    }

    // ---------------------------------------------------------------- apoio

    private async Task<DisplaySession?> FindByTokenAsync(string? deviceToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceToken) || deviceToken.Length > 128)
            return null;
        var hash = AuthService.HashToken(deviceToken);
        return await _db.DisplaySessions.AsNoTracking().FirstOrDefaultAsync(s => s.DeviceTokenHash == hash, ct);
    }

    private async Task<bool> IsShowableAsync(Guid? tournamentId, CancellationToken ct) =>
        tournamentId is not null
        && await _db.Tournaments.AnyAsync(t => t.Id == tournamentId && t.Status != Cancelled, ct);

    private async Task UnpairAsync(Guid sessionId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.DisplaySessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Status, Pending)
                .SetProperty(s => s.TournamentId, (Guid?)null)
                .SetProperty(s => s.PairedAt, (DateTimeOffset?)null)
                .SetProperty(s => s.UpdatedAt, now), ct);
    }

    // O código atual (se ainda vale) vira o anterior e continua aceito por mais
    // CodeLifetime: o celular que leu o QR pouco antes da renovação ainda pareia.
    // No UPDATE o lado direito lê os valores antigos da linha.
    private async Task RenewCodeAsync(Guid sessionId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var code = await NewUniqueCodeAsync(ct);
        DateTimeOffset? previousExpiry = now + CodeLifetime;
        await _db.DisplaySessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.PreviousPairingCode,
                    s => s.PairingCode != null && s.CodeExpiresAt > now ? s.PairingCode : null)
                .SetProperty(s => s.PreviousCodeExpiresAt,
                    s => s.PairingCode != null && s.CodeExpiresAt > now ? previousExpiry : null)
                .SetProperty(s => s.PairingCode, code)
                .SetProperty(s => s.CodeExpiresAt, now + CodeLifetime)
                .SetProperty(s => s.UpdatedAt, now), ct);
    }

    // Código atual no prazo, ou o anterior à última renovação ainda no prazo
    private static Expression<Func<DisplaySession, bool>> HasValidCode(string code, DateTimeOffset now) =>
        s => (s.PairingCode == code && s.CodeExpiresAt > now)
            || (s.PreviousPairingCode == code && s.PreviousCodeExpiresAt > now);

    /// <summary>
    /// TV livre (Pending) qualquer um pareia. TV já mostrando um torneio só troca quem é
    /// Admin, quem a pareou, ou dono/membro do home game do torneio ATUAL.
    /// </summary>
    private async Task<bool> CanReplaceAsync(DisplaySession session, Guid userId, bool isAdmin, CancellationToken ct)
    {
        if (session.Status != Paired || session.TournamentId is null)
            return true;
        if (isAdmin || session.PairedByUserId == userId)
            return true;

        var homeGameId = await _db.Tournaments
            .Where(t => t.Id == session.TournamentId && t.Status != Cancelled)
            .Select(t => (Guid?)t.HomeGameId)
            .FirstOrDefaultAsync(ct);
        // Torneio excluído ou cancelado: a TV já vai voltar para o QR no próximo status
        if (homeGameId is null)
            return true;

        return await HomeGameAccess.CanAccessAsync(_db, homeGameId.Value, userId, isAdmin: false, ct);
    }

    private async Task<DisplaySessionStatusResponse> ToStatusAsync(DisplaySession session, CancellationToken ct)
    {
        string? tournamentName = null;
        if (session.Status == Paired && session.TournamentId is not null)
            tournamentName = await _db.Tournaments.Where(t => t.Id == session.TournamentId)
                .Select(t => t.Name).FirstOrDefaultAsync(ct);

        return new DisplaySessionStatusResponse
        {
            Status = session.Status,
            PairingCode = session.PairingCode,
            CodeExpiresAt = session.CodeExpiresAt,
            TournamentId = session.Status == Paired ? session.TournamentId : null,
            TournamentName = tournamentName,
        };
    }

    // TVs esquecidas no QR por mais de 1 dia, ou paradas há 30 dias, somem
    private async Task CleanupAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var pendingCutoff = now - PendingLifetime;
        var idleCutoff = now - IdleLifetime;
        await _db.DisplaySessions
            .Where(s => (s.Status == Pending && s.LastSeenAt < pendingCutoff) || s.LastSeenAt < idleCutoff)
            .ExecuteDeleteAsync(ct);
    }

    private async Task<string> NewUniqueCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = string.Create(CodeLength, 0, (span, _) =>
            {
                for (var i = 0; i < span.Length; i++)
                    span[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            });
            // Nem atual nem anterior de outra TV: senão o código abriria a TV errada
            if (!await _db.DisplaySessions.AnyAsync(s => s.PairingCode == code || s.PreviousPairingCode == code, ct))
                return code;
        }
        throw new InvalidOperationException("Não foi possível gerar um código de pareamento. Tente de novo.");
    }

    // Aceita "k7m-2qx", "K7M 2QX" etc.
    private static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var normalized = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return normalized.Length == CodeLength ? normalized : null;
    }

    private static string NewDeviceToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static int StatusRank(string status) => status switch
    {
        nameof(TournamentStatus.InProgress) => 0,
        nameof(TournamentStatus.BreakSettlement) => 1,
        nameof(TournamentStatus.OpenForRegistration) => 2,
        nameof(TournamentStatus.Draft) => 3,
        _ => 4,
    };

    private static string? Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= 300 ? value : value[..300];
}
