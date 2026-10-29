using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Área do jogador (SPEC-20261029-1726, D13/D14 do plano de 2026-10-29): os torneios que a conta logada jogou e está
/// jogando, em QUALQUER home game, fora do filtro de tenant. A única autorização é o vínculo da SPEC-20261029-1725:
/// toda leitura parte de <see cref="MyEntries"/> (<c>Entry.Person.UserId == conta</c>) — nada de filtro por e-mail, nada
/// de inscrição de outra conta (nem por id direto) e, do torneio, só as contagens (entradas, jogando).
/// <para>
/// Entram as inscrições de torneio não cancelado e não excluído de home game não excluído. Jogador removido do cadastro
/// do home game (soft delete do <see cref="Person"/>) continua no torneio com o nome — e continua no histórico da conta
/// (mesma regra de <see cref="EntryQueries"/>). Torneio excluído some do histórico.
/// </para>
/// <para>
/// O que o organizador não tem no plano não aparece: sem Payments, devido/pago/saldo/PIX vêm null (sem o controle de
/// pagamentos o "deve" seria o valor cheio); sem Prizes, prêmio e premiação total; sem Tables, mesa e lugar. Bounty só no
/// torneio com bounty.
/// </para>
/// </summary>
public class MePlayerService
{
    /// <summary>Torneios recentes no resumo (Início).</summary>
    public const int RecentCount = 5;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    /// <summary>Rótulo do principal de dono Mesa quando o organizador é a própria conta.</summary>
    public const string OwnLabel = "Seus torneios";

    private static readonly string[] LiveStatuses =
    [
        nameof(TournamentStatus.OpenForRegistration),
        nameof(TournamentStatus.InProgress),
        nameof(TournamentStatus.BreakSettlement),
    ];

    private static readonly string[] ItmEntryStatuses =
    [
        nameof(EntryStatus.Awarded),
        nameof(EntryStatus.PaidOut),
    ];

    private readonly IAppDbContext _db;
    private readonly PlanService _plans;
    private readonly TimerService _timer;

    public MePlayerService(IAppDbContext db, PlanService plans, TimerService timer)
    {
        _db = db;
        _plans = plans;
        _timer = timer;
    }

    // ------------------------------------------------------------------ consultas públicas

    /// <summary>Resumo do Início ("Como jogador"). Null = conta inexistente.</summary>
    public async Task<MePlayerSummaryResponse?> GetSummaryAsync(Guid userId, CancellationToken ct = default)
    {
        var email = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);
        if (email is null)
            return null;

        var rows = await LoadRowsAsync(MyEntries(userId), ct);
        var tournaments = await BuildAsync(userId, rows, ct);

        var summary = new MePlayerSummaryResponse
        {
            Email = email,
            TournamentCount = tournaments.Count,
            Wins = tournaments.Count(t => t.FinalPosition == 1),
            InTheMoney = tournaments.Count(IsInTheMoney),
            OpenBalance = tournaments.Where(t => t.Balance > 0).Sum(t => t.Balance!.Value),
            OpenBalanceCount = tournaments.Count(t => t.Balance > 0),
            Recent = tournaments.Take(RecentCount).ToList(),
            Live = tournaments.Where(t => LiveStatuses.Contains(t.Status)).ToList(),
        };
        // Onde joga: o home game do torneio mais recente primeiro (a lista já vem por data, do mais novo)
        summary.HomeGames = tournaments
            .GroupBy(t => t.HomeGameId)
            .Select(g => new MePlayerHomeGame { Id = g.Key, Label = g.First().HomeGameLabel, TournamentCount = g.Count() })
            .ToList();
        return summary;
    }

    /// <summary>
    /// /meus-torneios: os torneios da conta por data (o mais novo primeiro), paginados por torneio, com filtro opcional por
    /// home game. Home game em que a conta não joga = lista vazia (não é erro: o filtro só restringe as inscrições dela).
    /// </summary>
    public async Task<PaginatedResponse<MyTournamentResponse>> GetTournamentsAsync(
        Guid userId, Guid? homeGameId, int page, int pageSize, CancellationToken ct = default)
    {
        var paging = Paging.Normalize(page, pageSize, DefaultPageSize, MaxPageSize);
        var entries = MyEntries(userId);
        if (homeGameId is { } hg)
            entries = entries.Where(e => e.Tournament.HomeGameId == hg);

        var tournamentIds = entries.Select(e => new { e.TournamentId, e.Tournament.Date }).Distinct();
        var total = await tournamentIds.CountAsync(ct);
        if (paging.IsPastEnd(total))
            return new PaginatedResponse<MyTournamentResponse>([], paging.Page, paging.PageSize, total);

        var pageIds = await tournamentIds
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.TournamentId)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(x => x.TournamentId)
            .ToListAsync(ct);

        var rows = await LoadRowsAsync(entries.Where(e => pageIds.Contains(e.TournamentId)), ct);
        var data = await BuildAsync(userId, rows, ct);
        return new PaginatedResponse<MyTournamentResponse>(data, paging.Page, paging.PageSize, total);
    }

    /// <summary>
    /// O torneio ao vivo para quem joga. Null = a conta não tem inscrição nele (ou ele foi cancelado/excluído) → 404, sem
    /// dizer se o torneio existe.
    /// </summary>
    public async Task<MyTournamentLiveResponse?> GetLiveAsync(Guid userId, Guid tournamentId, CancellationToken ct = default)
    {
        var rows = await LoadRowsAsync(MyEntries(userId).Where(e => e.TournamentId == tournamentId), ct);
        if (rows.Count == 0)
            return null;

        var info = await LoadTournamentsAsync([tournamentId], ct);
        if (!info.TryGetValue(tournamentId, out var t))
            return null;
        var features = await FeaturesAsync(info.Values, ct);
        var f = features[t.OwnerId];

        var live = new MyTournamentLiveResponse();
        Fill(live, t, rows, f, userId);

        var latest = Latest(rows);
        live.ShowsTables = f.Tables;
        if (f.Tables && latest.TableId is not null)
        {
            live.TableNumber = latest.TableNumber;
            live.TableName = latest.TableName;
            live.SeatNumber = latest.SeatNumber;
        }
        live.EliminatedAt = latest.EliminatedAt;
        live.BountyHead = IsKnockout(t.KnockoutType) ? latest.BountyHead : null;
        live.PrizePool = f.Prizes ? t.NetPrizePool : null;
        live.PixKey = f.Payments && !string.IsNullOrWhiteSpace(t.ResponsiblePixKey) ? t.ResponsiblePixKey : null;
        live.BlindLevels = await LoadBlindLevelsAsync(tournamentId, t.BlindStructureId, ct);

        // Relógio: a mesma leitura do painel e do telão (avança o nível sozinho, server-authoritative). Torneio sem
        // estrutura de blinds não tem relógio.
        if (live.BlindLevels.Count > 0)
        {
            try
            {
                live.Timer = await _timer.GetTimerStateAsync(tournamentId, ct);
                // A leitura pode ter avançado o nível: o "nível atual" da linha acompanha
                live.CurrentLevel = live.Timer.CurrentLevel;
            }
            catch (DomainException)
            {
                live.Timer = null;
            }
        }

        return live;
    }

    // ------------------------------------------------------------------ base: só as inscrições da conta

    /// <summary>
    /// Inscrições cujo jogador está vinculado à conta. <c>IgnoreQueryFilters</c> solta o jogador removido do cadastro (ele
    /// continua no torneio), então torneio e home game excluídos saem aqui à mão; cancelado não entra.
    /// </summary>
    private IQueryable<TournamentEntry> MyEntries(Guid userId)
    {
        var cancelled = nameof(TournamentStatus.Cancelled);
        return _db.TournamentEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Person.UserId == userId
                && e.Tournament.DeletedAt == null
                && e.Tournament.Status != cancelled
                && e.Tournament.HomeGame.DeletedAt == null);
    }

    private sealed class EntryRow
    {
        public Guid TournamentId { get; init; }
        public int EntryNumber { get; init; }
        public DateTimeOffset RegisteredAt { get; init; }
        public string Status { get; init; } = string.Empty;
        public int? FinalPosition { get; init; }
        public int RebuyCount { get; init; }
        public int AddonCount { get; init; }
        public decimal TotalDue { get; init; }
        public decimal TotalPaid { get; init; }
        public decimal Balance { get; init; }
        public decimal? PrizeAmount { get; init; }
        public decimal BountyHead { get; init; }
        public decimal BountyWon { get; init; }
        public Guid? TableId { get; init; }
        public int? TableNumber { get; init; }
        public string? TableName { get; init; }
        public int? SeatNumber { get; init; }
        public DateTimeOffset? EliminatedAt { get; init; }
    }

    private sealed class TournamentInfo
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public DateTimeOffset Date { get; init; }
        public string Status { get; init; } = string.Empty;
        public Guid HomeGameId { get; init; }
        public string HomeGameName { get; init; } = string.Empty;
        public bool HomeGameIsPrimary { get; init; }
        public Guid OwnerId { get; init; }
        public string? OwnerName { get; init; }
        public string KnockoutType { get; init; } = string.Empty;
        public int TotalEntries { get; init; }
        public int PlayersRemaining { get; init; }
        public int? CurrentLevel { get; init; }
        public bool IsTimerRunning { get; init; }
        public decimal NetPrizePool { get; init; }
        public string? ResponsiblePixKey { get; init; }
        public Guid? BlindStructureId { get; init; }
    }

    private sealed record OwnerFeatures(string Plan, bool Payments, bool Prizes, bool Tables);

    private static Task<List<EntryRow>> LoadRowsAsync(IQueryable<TournamentEntry> entries, CancellationToken ct) =>
        entries.Select(e => new EntryRow
        {
            TournamentId = e.TournamentId,
            EntryNumber = e.EntryNumber,
            RegisteredAt = e.RegisteredAt,
            Status = e.Status,
            FinalPosition = e.FinalPosition,
            RebuyCount = e.RebuyCount,
            AddonCount = e.AddonCount,
            TotalDue = e.TotalDue,
            TotalPaid = e.TotalPaid,
            Balance = e.Balance,
            PrizeAmount = e.PrizeAmount,
            BountyHead = e.BountyHead,
            BountyWon = e.BountyWon,
            TableId = e.TableId,
            TableNumber = e.Table != null ? e.Table.TableNumber : null,
            TableName = e.Table != null ? e.Table.TableName : null,
            SeatNumber = e.SeatNumber,
            EliminatedAt = e.EliminatedAt,
        }).ToListAsync(ct);

    /// <summary>Dados dos torneios (sem nada de outros jogadores além das contagens denormalizadas).</summary>
    private async Task<Dictionary<Guid, TournamentInfo>> LoadTournamentsAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];
        return await _db.Tournaments.IgnoreQueryFilters().AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.DeletedAt == null && t.HomeGame.DeletedAt == null)
            .Select(t => new TournamentInfo
            {
                Id = t.Id,
                Name = t.Name,
                Date = t.Date,
                Status = t.Status,
                HomeGameId = t.HomeGameId,
                HomeGameName = t.HomeGame.Name,
                HomeGameIsPrimary = t.HomeGame.IsPrimary,
                OwnerId = t.HomeGame.OwnerId,
                OwnerName = t.HomeGame.Owner.FullName,
                KnockoutType = t.KnockoutType,
                TotalEntries = t.TotalEntries,
                PlayersRemaining = t.PlayersRemaining,
                CurrentLevel = t.CurrentLevel,
                IsTimerRunning = t.IsTimerRunning,
                NetPrizePool = t.NetPrizePool,
                ResponsiblePixKey = t.ResponsiblePixKey,
                BlindStructureId = t.BlindStructureId,
            })
            .ToDictionaryAsync(t => t.Id, ct);
    }

    /// <summary>Recursos do plano efetivo de cada organizador (cache de 30 s do PlanService).</summary>
    private async Task<Dictionary<Guid, OwnerFeatures>> FeaturesAsync(IEnumerable<TournamentInfo> tournaments, CancellationToken ct)
    {
        var result = new Dictionary<Guid, OwnerFeatures>();
        foreach (var ownerId in tournaments.Select(t => t.OwnerId).Distinct())
        {
            var plan = await _plans.GetUserEffectivePlanAsync(ownerId, ct);
            result[ownerId] = new OwnerFeatures(
                plan,
                _plans.Allows(plan, PlanFeature.Payments),
                _plans.Allows(plan, PlanFeature.Prizes),
                _plans.Allows(plan, PlanFeature.Tables));
        }
        return result;
    }

    /// <summary>Uma linha por torneio, pela data (o mais novo primeiro).</summary>
    private async Task<List<MyTournamentResponse>> BuildAsync(Guid userId, List<EntryRow> rows, CancellationToken ct)
    {
        var byTournament = rows.GroupBy(r => r.TournamentId).ToDictionary(g => g.Key, g => g.ToList());
        var info = await LoadTournamentsAsync([.. byTournament.Keys], ct);
        var features = await FeaturesAsync(info.Values, ct);

        return info.Values
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .Select(t =>
            {
                var item = new MyTournamentResponse();
                Fill(item, t, byTournament[t.Id], features[t.OwnerId], userId);
                return item;
            })
            .ToList();
    }

    private static void Fill(MyTournamentResponse item, TournamentInfo t, List<EntryRow> rows, OwnerFeatures f, Guid userId)
    {
        var latest = Latest(rows);
        var knockout = IsKnockout(t.KnockoutType);

        item.TournamentId = t.Id;
        item.Name = t.Name;
        item.Date = t.Date;
        item.Status = t.Status;
        item.HomeGameId = t.HomeGameId;
        item.HomeGameLabel = LabelFor(t, f.Plan, userId);
        item.KnockoutType = t.KnockoutType;
        item.TotalEntries = t.TotalEntries;
        item.PlayersRemaining = t.PlayersRemaining;
        item.CurrentLevel = t.CurrentLevel;
        item.IsTimerRunning = t.IsTimerRunning;
        item.ShowsPayments = f.Payments;
        item.ShowsPrizes = f.Prizes;

        item.EntryCount = rows.Count;
        item.EntryStatus = latest.Status;
        item.FinalPosition = latest.FinalPosition;
        item.RebuyCount = rows.Sum(r => r.RebuyCount);
        item.AddonCount = rows.Sum(r => r.AddonCount);

        if (f.Payments)
        {
            var due = rows.Sum(r => r.TotalDue);
            var paid = rows.Sum(r => r.TotalPaid);
            var balance = rows.Sum(r => r.Balance);
            item.TotalDue = due;
            item.TotalPaid = paid;
            item.Balance = balance;
            item.PaymentStatus = PaymentStatusOf(balance, paid);
        }

        if (f.Prizes)
        {
            var prizes = rows.Where(r => r.PrizeAmount is not null).ToList();
            item.PrizeAmount = prizes.Count == 0 ? null : prizes.Sum(r => r.PrizeAmount!.Value);
        }

        item.BountyWon = knockout ? rows.Sum(r => r.BountyWon) : null;
    }

    /// <summary>A inscrição mais recente (reentrada: a última).</summary>
    private static EntryRow Latest(List<EntryRow> rows) =>
        rows.OrderByDescending(r => r.EntryNumber).ThenByDescending(r => r.RegisteredAt).First();

    private static bool IsKnockout(string knockoutType) =>
        !string.IsNullOrEmpty(knockoutType) && knockoutType != nameof(KnockoutType.None);

    private static bool IsInTheMoney(MyTournamentResponse t) =>
        ItmEntryStatuses.Contains(t.EntryStatus) || t.PrizeAmount > 0;

    /// <summary>A mesma regra do saldo da inscrição (EntryService.RecalculateBalance), sobre os valores somados.</summary>
    private static string PaymentStatusOf(decimal balance, decimal paid) => balance switch
    {
        0 => nameof(PaymentStatus.Paid),
        > 0 when paid > 0 => nameof(PaymentStatus.PartiallyPaid),
        < 0 => nameof(PaymentStatus.Overpaid),
        _ => nameof(PaymentStatus.Pending),
    };

    /// <summary>
    /// "Onde joguei": o nome do clube (<see cref="HomeGamePrimary.DisplayNameFor"/>); no principal de dono Mesa, que não
    /// tem nome de clube, "Torneios de &lt;organizador&gt;" (ou "Seus torneios" quando o organizador é a própria conta).
    /// </summary>
    private static string LabelFor(TournamentInfo t, string ownerPlan, Guid userId)
    {
        var display = HomeGamePrimary.DisplayNameFor(t.HomeGameIsPrimary, ownerPlan, t.HomeGameName);
        if (display is not null)
            return display;
        if (t.OwnerId == userId)
            return OwnLabel;
        var owner = t.OwnerName?.Trim();
        return string.IsNullOrEmpty(owner) ? t.HomeGameName : $"Torneios de {owner}";
    }

    /// <summary>Níveis do torneio (os próprios; sem eles, os da estrutura — dado antigo), como o TimerService lê.</summary>
    private async Task<List<MyBlindLevel>> LoadBlindLevelsAsync(Guid tournamentId, Guid? blindStructureId, CancellationToken ct)
    {
        var own = await _db.TournamentBlindLevels.AsNoTracking()
            .Where(l => l.TournamentId == tournamentId)
            .OrderBy(l => l.LevelNumber)
            .Select(l => new MyBlindLevel
            {
                LevelNumber = l.LevelNumber,
                SmallBlind = l.SmallBlind,
                BigBlind = l.BigBlind,
                Ante = l.Ante,
                DurationMinutes = l.DurationMinutes,
                IsBreak = l.IsBreak,
            })
            .ToListAsync(ct);
        if (own.Count > 0 || blindStructureId is null)
            return own;

        return await _db.BlindLevels.AsNoTracking()
            .Where(l => l.BlindStructureId == blindStructureId)
            .OrderBy(l => l.LevelNumber)
            .Select(l => new MyBlindLevel
            {
                LevelNumber = l.LevelNumber,
                SmallBlind = l.SmallBlind,
                BigBlind = l.BigBlind,
                Ante = l.Ante,
                DurationMinutes = l.DurationMinutes,
                IsBreak = l.IsBreak,
            })
            .ToListAsync(ct);
    }
}
