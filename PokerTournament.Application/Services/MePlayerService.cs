using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Application.Timebank;
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
/// pagamentos o "deve" seria o valor cheio); sem Prizes, prêmio, premiação total e bolha; sem Tables, mesa e lugar. Bounty
/// só no torneio com bounty.
/// </para>
/// <para>
/// SPEC-20261108-1633 (área completa, D2/D3 do plano de 2026-11-08): o que o jogador vê DOS OUTROS é só o que é público
/// dentro do clube — nome público (<see cref="RankingLeaderboardService.PublicName(Person)"/>: apelido, ou primeiro nome +
/// inicial) e foto exibida — no ranking (o mesmo DTO da página pública /r/{slug}), nos lugares da mesa dele e nas
/// eliminações (quem o eliminou, quem ele eliminou). Nunca dinheiro, contato ou inscrição de terceiros.
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

    /// <summary>
    /// Até quantas horas antes de agora um torneio agendado ainda conta como "próximo" (o organizador costuma marcar a data
    /// do dia, e o torneio começa mais tarde).
    /// </summary>
    public const int NextTournamentGraceHours = 12;

    private static readonly string[] LiveStatuses =
    [
        nameof(TournamentStatus.OpenForRegistration),
        nameof(TournamentStatus.InProgress),
        nameof(TournamentStatus.BreakSettlement),
    ];

    /// <summary>Ainda não começou: candidato a "próximo torneio".</summary>
    private static readonly string[] NotStartedStatuses =
    [
        nameof(TournamentStatus.Draft),
        nameof(TournamentStatus.OpenForRegistration),
    ];

    private static readonly string[] ItmEntryStatuses =
    [
        nameof(EntryStatus.Awarded),
        nameof(EntryStatus.PaidOut),
    ];

    private static readonly string[] PlayingEntryStatuses =
    [
        nameof(EntryStatus.Registered),
        nameof(EntryStatus.Active),
    ];

    private readonly IAppDbContext _db;
    private readonly PlanService _plans;
    private readonly TimerService _timer;
    private readonly RankingLeaderboardService _leaderboard;
    private readonly PrizeService _prizes;
    private readonly JoinRequestService _joins;

    public MePlayerService(
        IAppDbContext db, PlanService plans, TimerService timer, RankingLeaderboardService leaderboard, PrizeService prizes,
        JoinRequestService joins)
    {
        _db = db;
        _plans = plans;
        _timer = timer;
        _leaderboard = leaderboard;
        _prizes = prizes;
        _joins = joins;
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

        // "Próximos" (SPEC-20261108-1639): pedidos de vaga abertos e inscrições futuras — também para quem ainda não jogou
        summary.Upcoming = await _joins.UpcomingAsync(userId, ct);

        if (tournaments.Count > 0)
        {
            summary.NextTournament = await NextTournamentAsync(userId, ct);
            summary.Rankings = (await MyRankingsAsync(userId, null, ct))
                .Select(r => new MyRankingPositionResponse
                {
                    RankingId = r.RankingId,
                    Name = r.Name,
                    Season = r.Season,
                    HomeGameId = r.HomeGameId,
                    HomeGameLabel = r.HomeGameLabel,
                    Position = r.MyPosition,
                    PlayerCount = r.Leaderboard.Count,
                    TotalPoints = r.MyTotalPoints,
                    TotalStages = r.TotalStages,
                })
                .ToList();
        }
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
        var eliminations = await LoadEliminationsAsync([tournamentId], ct);
        await EnrichAsync([live], info, rows, eliminations, userId, ct);

        var latest = Latest(rows);
        var myEntryIds = rows.Select(r => r.EntryId).ToHashSet();
        var isLive = LiveStatuses.Contains(t.Status);
        live.ServerNow = DateTimeOffset.UtcNow;
        live.ShowsTables = f.Tables;
        if (f.Tables && latest.TableId is not null)
        {
            live.TableNumber = latest.TableNumber;
            live.TableName = latest.TableName;
            live.SeatNumber = latest.SeatNumber;
            if (PlayingEntryStatuses.Contains(latest.Status) && t.Status != nameof(TournamentStatus.Finished))
                live.Table = await LoadTableAsync(latest.TableId.Value, myEntryIds, ct);
        }
        live.EliminatedAt = latest.EliminatedAt;
        live.BountyHead = IsKnockout(t.KnockoutType) ? latest.BountyHead : null;
        live.PrizePool = f.Prizes ? t.NetPrizePool : null;
        live.PixKey = f.Payments && !string.IsNullOrWhiteSpace(t.ResponsiblePixKey) ? t.ResponsiblePixKey : null;
        live.BlindLevels = await LoadBlindLevelsAsync(tournamentId, t.BlindStructureId, ct);

        // Timebank (SPEC-20261108-1633): os da inscrição mais recente e o "você está no tempo"
        if (t.TimebankCount is > 0)
        {
            live.TimebankCount = t.TimebankCount;
            live.TimebankSeconds = t.TimebankSeconds ?? TimebankRules.DefaultSeconds;
            live.TimebanksRemaining = latest.TimebanksRemaining;
            if (isLive)
                live.OnTheClockUntil = await OnTheClockUntilAsync(tournamentId, live.TimebankSeconds.Value, myEntryIds, ct);
        }

        // Bolha (SPEC-20261108-1633): só com Prizes e com o torneio acontecendo
        if (f.Prizes && isLive)
            live.Bubble = await LoadBubbleAsync(t, latest, ct);

        // "Chamar o organizador": o WhatsApp de quem criou o torneio, senão o do dono do clube
        var organizer = await OrganizerAsync(t, ct);
        live.OrganizerWhatsapp = organizer.Whatsapp;
        live.OrganizerName = organizer.Name;

        // Eliminações que a conta fez (nome público de quem saiu)
        live.Eliminations = eliminations
            .Where(e => e.EliminatedByEntryId is { } by && myEntryIds.Contains(by))
            .OrderByDescending(e => e.EliminatedAt)
            .Select(e => new MyEliminationResponse
            {
                DisplayName = RankingLeaderboardService.PublicName(e.VictimNickname, e.VictimName),
                Position = e.Position,
                At = e.EliminatedAt,
            })
            .ToList();

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

    /// <summary>
    /// Os lançamentos das inscrições da conta no torneio (SPEC-20261108-1633): transações, eliminação dela e as que ela
    /// fez, atividades de mesa/lugar/timebank dela — do mais recente para o mais antigo. Null = sem inscrição (404).
    /// </summary>
    public async Task<List<MyHistoryItemResponse>?> GetHistoryAsync(Guid userId, Guid tournamentId, CancellationToken ct = default)
    {
        var rows = await LoadRowsAsync(MyEntries(userId).Where(e => e.TournamentId == tournamentId), ct);
        if (rows.Count == 0)
            return null;
        var myEntryIds = rows.Select(r => r.EntryId).ToList();
        var mine = myEntryIds.ToHashSet();

        var refund = nameof(TransactionType.Refund);
        var transactions = await _db.Transactions.AsNoTracking()
            .Include(t => t.PixDestination)
            .Where(t => t.TournamentId == tournamentId && t.EntryId != null && myEntryIds.Contains(t.EntryId.Value) && t.Type != refund)
            .ToListAsync(ct);

        var eliminations = await LoadEliminationsAsync([tournamentId], ct);

        var activities = await _db.TournamentActivities.AsNoTracking()
            .Where(a => a.TournamentId == tournamentId && a.EntryId != null && myEntryIds.Contains(a.EntryId.Value))
            .ToListAsync(ct);

        var items = new List<MyHistoryItemResponse>();
        foreach (var t in transactions)
        {
            items.Add(new MyHistoryItemResponse
            {
                Kind = t.Type,
                Description = TournamentHistoryService.DescribeTransaction(t),
                Amount = t.Amount,
                At = t.CreatedAt,
                Undone = t.ReversedAt != null,
            });
        }
        foreach (var e in eliminations)
        {
            if (mine.Contains(e.EntryId))
            {
                var text = e.Position.HasValue ? $"Você saiu em {e.Position}º lugar" : "Você saiu do torneio";
                if (e.EliminatedByEntryId is not null)
                    text += $" · eliminado por {RankingLeaderboardService.PublicName(e.EliminatorNickname, e.EliminatorName ?? string.Empty)}";
                items.Add(new MyHistoryItemResponse { Kind = "Elimination", Description = text, At = e.EliminatedAt, Undone = e.Corrected });
            }
            else if (e.EliminatedByEntryId is { } by && mine.Contains(by))
            {
                var victim = RankingLeaderboardService.PublicName(e.VictimNickname, e.VictimName);
                var text = e.Position.HasValue ? $"Você eliminou {victim} ({e.Position}º lugar)" : $"Você eliminou {victim}";
                items.Add(new MyHistoryItemResponse { Kind = "Knockout", Description = text, At = e.EliminatedAt, Undone = e.Corrected });
            }
        }
        foreach (var a in activities)
        {
            items.Add(new MyHistoryItemResponse
            {
                Kind = a.Type,
                Description = a.Description,
                At = a.CreatedAt,
                Undone = a.UndoneAt != null,
            });
        }

        return items
            .OrderByDescending(i => i.At)
            .ThenBy(i => i.Kind, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// /meus-rankings: cada ranking em que a conta tem inscrição vinculada em algum torneio (não cancelado, não excluído),
    /// com a classificação completa e a linha dela. Conta sem inscrição = lista vazia.
    /// </summary>
    public Task<List<MyRankingResponse>> GetRankingsAsync(Guid userId, CancellationToken ct = default) =>
        MyRankingsAsync(userId, null, ct);

    /// <summary>Um ranking da conta. Null = a conta não tem inscrição em nenhum torneio dele (404, sem dizer se existe).</summary>
    public async Task<MyRankingResponse?> GetRankingAsync(Guid userId, Guid rankingId, CancellationToken ct = default) =>
        (await MyRankingsAsync(userId, rankingId, ct)).FirstOrDefault();

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
        public Guid EntryId { get; init; }
        public Guid PersonId { get; init; }
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
        public int TimebanksRemaining { get; init; }
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
        public Guid? CreatedBy { get; init; }
        public string KnockoutType { get; init; } = string.Empty;
        public int TotalEntries { get; init; }
        public int PlayersRemaining { get; init; }
        public int? CurrentLevel { get; init; }
        public bool IsTimerRunning { get; init; }
        public decimal NetPrizePool { get; init; }
        public bool PrizeConfirmed { get; init; }
        public string? ResponsiblePixKey { get; init; }
        public Guid? BlindStructureId { get; init; }
        public int? TimebankCount { get; init; }
        public int? TimebankSeconds { get; init; }
        public Guid? RankingId { get; init; }
        public string? RankingName { get; init; }
    }

    /// <summary>Uma eliminação valendo ou corrigida, com os nomes públicos dos dois lados (nunca o nome completo de terceiros na resposta).</summary>
    private sealed class EliminationRow
    {
        public Guid TournamentId { get; init; }
        public Guid EntryId { get; init; }
        public Guid? EliminatedByEntryId { get; init; }
        public int? Position { get; init; }
        public DateTimeOffset EliminatedAt { get; init; }
        public bool Corrected { get; init; }
        public string VictimName { get; init; } = string.Empty;
        public string? VictimNickname { get; init; }
        public string? EliminatorName { get; init; }
        public string? EliminatorNickname { get; init; }
    }

    private sealed record OwnerFeatures(string Plan, bool Payments, bool Prizes, bool Tables);

    private static Task<List<EntryRow>> LoadRowsAsync(IQueryable<TournamentEntry> entries, CancellationToken ct) =>
        entries.Select(e => new EntryRow
        {
            EntryId = e.Id,
            PersonId = e.PersonId,
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
            TimebanksRemaining = e.TimebanksRemaining,
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
                CreatedBy = t.CreatedBy,
                KnockoutType = t.KnockoutType,
                TotalEntries = t.TotalEntries,
                PlayersRemaining = t.PlayersRemaining,
                CurrentLevel = t.CurrentLevel,
                IsTimerRunning = t.IsTimerRunning,
                NetPrizePool = t.NetPrizePool,
                PrizeConfirmed = t.PrizeConfirmed,
                ResponsiblePixKey = t.ResponsiblePixKey,
                BlindStructureId = t.BlindStructureId,
                TimebankCount = t.TimebankCount,
                TimebankSeconds = t.TimebankSeconds,
                // Ranking excluído (soft delete) não aparece para o jogador
                RankingId = t.Ranking != null && t.Ranking.DeletedAt == null ? t.RankingId : null,
                RankingName = t.Ranking != null && t.Ranking.DeletedAt == null ? t.Ranking.Name : null,
            })
            .ToDictionaryAsync(t => t.Id, ct);
    }

    /// <summary>Eliminações dos torneios (valendo e corrigidas), com os nomes dos dois lados — jogador removido do cadastro incluído.</summary>
    private async Task<List<EliminationRow>> LoadEliminationsAsync(List<Guid> tournamentIds, CancellationToken ct)
    {
        if (tournamentIds.Count == 0)
            return [];
        return await _db.Eliminations.IgnoreQueryFilters().AsNoTracking()
            .Where(e => tournamentIds.Contains(e.TournamentId))
            .Select(e => new EliminationRow
            {
                TournamentId = e.TournamentId,
                EntryId = e.EntryId,
                EliminatedByEntryId = e.EliminatedByEntryId,
                Position = e.Position,
                EliminatedAt = e.EliminatedAt,
                Corrected = e.Corrected,
                VictimName = e.Entry.Person.FullName,
                VictimNickname = e.Entry.Person.Nickname,
                EliminatorName = e.EliminatedByEntry != null ? e.EliminatedByEntry.Person.FullName : null,
                EliminatorNickname = e.EliminatedByEntry != null ? e.EliminatedByEntry.Person.Nickname : null,
            })
            .ToListAsync(ct);
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

        var items = info.Values
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .Select(t =>
            {
                var item = new MyTournamentResponse();
                Fill(item, t, byTournament[t.Id], features[t.OwnerId], userId);
                return item;
            })
            .ToList();

        var eliminations = await LoadEliminationsAsync([.. info.Keys], ct);
        await EnrichAsync(items, info, rows, eliminations, userId, ct);
        return items;
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
        item.RankingId = t.RankingId;
        item.RankingName = t.RankingName;

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

    /// <summary>
    /// SPEC-20261108-1633: por torneio, os pontos da conta no ranking e a posição dela logo depois dele (classificação só
    /// com as etapas até aquele torneio — o mesmo <see cref="RankingLeaderboardService"/> do organizador e da página
    /// pública), quem a eliminou e quantos ela eliminou. Um carregamento do ranking por ranking, não por torneio.
    /// </summary>
    private async Task EnrichAsync(
        List<MyTournamentResponse> items, Dictionary<Guid, TournamentInfo> info, List<EntryRow> rows,
        List<EliminationRow> eliminations, Guid userId, CancellationToken ct)
    {
        var myEntryIds = rows.Select(r => r.EntryId).ToHashSet();
        var myPersonIds = rows.Select(r => r.PersonId).ToHashSet();

        foreach (var item in items)
        {
            var mineHere = eliminations.Where(e => e.TournamentId == item.TournamentId && !e.Corrected).ToList();
            var fall = mineHere.FirstOrDefault(e => myEntryIds.Contains(e.EntryId) && e.EliminatedByEntryId is not null);
            if (fall is not null)
                item.EliminatedByName = RankingLeaderboardService.PublicName(fall.EliminatorNickname, fall.EliminatorName ?? string.Empty);
            item.EliminationsCount = mineHere.Count(e => e.EliminatedByEntryId is { } by && myEntryIds.Contains(by));
        }

        var finished = nameof(TournamentStatus.Finished);
        var rankingIds = items
            .Where(i => i.RankingId is not null && i.Status == finished)
            .Select(i => i.RankingId!.Value)
            .Distinct()
            .ToList();
        if (rankingIds.Count == 0)
            return;

        var rankings = await _db.Rankings.AsNoTracking()
            .Where(r => rankingIds.Contains(r.Id))
            .ToListAsync(ct);

        foreach (var ranking in rankings)
        {
            var data = await _leaderboard.LoadAsync(ranking, ct);
            // Ordem das etapas = a da página pública (data, depois id)
            var ordered = data.Stages.OrderBy(s => s.Date).ThenBy(s => s.Id).ToList();
            foreach (var item in items.Where(i => i.RankingId == ranking.Id && i.Status == finished))
            {
                var index = ordered.FindIndex(s => s.Id == item.TournamentId);
                if (index < 0)
                    continue;
                var upTo = ordered.Take(index + 1).Select(s => s.Id).ToHashSet();
                var after = _leaderboard.Compute(ranking, data, upTo);
                var mine = after.Players.Where(p => myPersonIds.Contains(p.Person.Id)).OrderBy(p => p.Position).FirstOrDefault();
                if (mine is null)
                    continue;
                item.RankingPositionAfter = mine.Position;
                item.RankingPlayersAfter = after.Players.Count;
                var points = after.Players
                    .Where(p => myPersonIds.Contains(p.Person.Id))
                    .SelectMany(p => p.Results)
                    .Where(r => r.TournamentId == item.TournamentId)
                    .Select(r => r.Points)
                    .ToList();
                item.RankingPoints = points.Count == 0 ? null : points.Sum();
            }
        }
    }

    // ------------------------------------------------------------------ SPEC-20261108-1633: rankings, próximo torneio, mesa, bolha

    /// <summary>
    /// Os rankings de que a conta participa: todo ranking (não excluído) com algum torneio em que ela tem inscrição
    /// vinculada. <paramref name="onlyRankingId"/> restringe a um (vazio se a conta não participa dele).
    /// </summary>
    private async Task<List<MyRankingResponse>> MyRankingsAsync(Guid userId, Guid? onlyRankingId, CancellationToken ct)
    {
        var mine = MyEntries(userId).Where(e => e.Tournament.RankingId != null);
        if (onlyRankingId is { } only)
            mine = mine.Where(e => e.Tournament.RankingId == only);
        var pairs = await mine
            .Select(e => new { RankingId = e.Tournament.RankingId!.Value, e.PersonId })
            .Distinct()
            .ToListAsync(ct);
        if (pairs.Count == 0)
            return [];

        var rankingIds = pairs.Select(p => p.RankingId).Distinct().ToList();
        var rankings = await _db.Rankings.AsNoTracking()
            .Include(r => r.HomeGame).ThenInclude(h => h.Owner)
            .Where(r => rankingIds.Contains(r.Id) && r.HomeGame.DeletedAt == null)
            .ToListAsync(ct);

        var result = new List<MyRankingResponse>();
        foreach (var ranking in rankings)
        {
            var standings = await _leaderboard.ComputeAsync(ranking, ct);
            var dto = await _leaderboard.BuildPublicAsync(ranking, standings, ct);
            var myPersonIds = pairs.Where(p => p.RankingId == ranking.Id).Select(p => p.PersonId).Distinct().ToList();
            var myRows = standings.Players.Where(p => myPersonIds.Contains(p.Person.Id)).OrderBy(p => p.Position).ToList();
            var ownerPlan = await _plans.GetUserEffectivePlanAsync(ranking.HomeGame.OwnerId, ct);
            var item = new MyRankingResponse
            {
                RankingId = ranking.Id,
                HomeGameId = ranking.HomeGameId,
                HomeGameLabel = LabelFor(ranking.HomeGame.IsPrimary, ownerPlan, ranking.HomeGame.Name, ranking.HomeGame.OwnerId, ranking.HomeGame.Owner.FullName, userId),
                MyPersonIds = myPersonIds,
                MyPosition = myRows.FirstOrDefault()?.Position,
                MyTotalPoints = myRows.Sum(p => p.TotalPoints),
                MyDiscardedPoints = myRows.Sum(p => p.DiscardedPoints),
                MyTournamentsPlayed = myRows.Sum(p => p.TournamentsPlayed),
            };
            CopyPublic(dto, item);
            result.Add(item);
        }

        return result
            .OrderBy(r => r.HomeGameLabel, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(r => r.Stages.Count == 0 ? DateTimeOffset.MinValue : r.Stages.Max(s => s.Date))
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void CopyPublic(PublicRankingResponse from, MyRankingResponse to)
    {
        to.Slug = from.Slug;
        to.Name = from.Name;
        to.Season = from.Season;
        to.HomeGameName = from.HomeGameName;
        to.AccumulatedPrize = from.AccumulatedPrize;
        to.DiscardCount = from.DiscardCount;
        to.TotalStages = from.TotalStages;
        to.CountedSlots = from.CountedSlots;
        to.PointsDependOnFieldSize = from.PointsDependOnFieldSize;
        to.Stages = from.Stages;
        to.Leaderboard = from.Leaderboard;
        to.GeneratedAt = from.GeneratedAt;
        to.Plan = from.Plan;
        to.ShowBadge = from.ShowBadge;
        to.ClubLogoUrl = from.ClubLogoUrl;
    }

    /// <summary>
    /// O próximo torneio dos clubes onde a conta joga: agendado ou com inscrições abertas, de hoje em diante (com a folga
    /// de <see cref="NextTournamentGraceHours"/> horas, porque a data costuma ser a do dia), o mais próximo primeiro.
    /// Só bairro/cidade do clube, nunca a rua (D6 do plano).
    /// </summary>
    private async Task<MyNextTournamentResponse?> NextTournamentAsync(Guid userId, CancellationToken ct)
    {
        var homeGameIds = await MyEntries(userId).Select(e => e.Tournament.HomeGameId).Distinct().ToListAsync(ct);
        if (homeGameIds.Count == 0)
            return null;
        var since = DateTimeOffset.UtcNow.AddHours(-NextTournamentGraceHours);
        var next = await _db.Tournaments.AsNoTracking()
            .Where(t => homeGameIds.Contains(t.HomeGameId) && NotStartedStatuses.Contains(t.Status) && t.Date >= since)
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Date,
                t.Status,
                t.HomeGameId,
                HomeGameName = t.HomeGame.Name,
                HomeGameIsPrimary = t.HomeGame.IsPrimary,
                OwnerId = t.HomeGame.OwnerId,
                OwnerName = t.HomeGame.Owner.FullName,
                t.BuyInAmount,
                StructureName = t.BlindStructure != null ? t.BlindStructure.Name : null,
                OwnLevels = t.BlindLevels.Any(),
                t.HomeGame.District,
                t.HomeGame.City,
                t.HomeGame.State,
                Registered = t.Entries.Any(e => e.Person.UserId == userId),
            })
            .FirstOrDefaultAsync(ct);
        if (next is null)
            return null;

        var ownerPlan = await _plans.GetUserEffectivePlanAsync(next.OwnerId, ct);
        var place = new[] { next.District, next.City }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
        if (place.Count == 1 && !string.IsNullOrWhiteSpace(next.State) && !string.IsNullOrWhiteSpace(next.City))
            place.Add(next.State!.Trim().ToUpperInvariant());
        return new MyNextTournamentResponse
        {
            TournamentId = next.Id,
            Name = next.Name,
            Date = next.Date,
            Status = next.Status,
            HomeGameId = next.HomeGameId,
            HomeGameLabel = LabelFor(next.HomeGameIsPrimary, ownerPlan, next.HomeGameName, next.OwnerId, next.OwnerName, userId),
            BuyInAmount = next.BuyInAmount,
            StructureName = next.OwnLevels ? null : next.StructureName,
            LocationSummary = place.Count == 0 ? null : string.Join(", ", place),
            Registered = next.Registered,
        };
    }

    /// <summary>A mesa da conta: lugares 1..MaxSeats; nos ocupados por quem ainda joga, nome público e foto exibida (D3).</summary>
    private async Task<MyTableResponse?> LoadTableAsync(Guid tableId, HashSet<Guid> myEntryIds, CancellationToken ct)
    {
        var table = await _db.TournamentTables.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == tableId && t.IsActive)
            .Select(t => new { t.Id, t.TableNumber, t.TableName, t.MaxSeats })
            .FirstOrDefaultAsync(ct);
        if (table is null)
            return null;

        var seated = await _db.TournamentEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TableId == tableId && PlayingEntryStatuses.Contains(e.Status) && e.SeatNumber != null)
            .Select(e => new
            {
                e.Id,
                Seat = e.SeatNumber!.Value,
                e.Person.Nickname,
                e.Person.FullName,
                PhotoUrl = PlayerPhoto.Of(e.Person.User != null ? e.Person.User.PhotoUrl : null, e.Person.PhotoUrl),
            })
            .ToListAsync(ct);
        var bySeat = seated.GroupBy(s => s.Seat).ToDictionary(g => g.Key, g => g.First());

        var result = new MyTableResponse
        {
            TableId = table.Id,
            TableNumber = table.TableNumber,
            TableName = table.TableName,
            MaxSeats = table.MaxSeats,
            MySeat = seated.FirstOrDefault(s => myEntryIds.Contains(s.Id))?.Seat,
        };
        for (var seat = 1; seat <= table.MaxSeats; seat++)
        {
            if (bySeat.TryGetValue(seat, out var p))
            {
                result.Seats.Add(new MyTableSeatResponse
                {
                    SeatNumber = seat,
                    IsMe = myEntryIds.Contains(p.Id),
                    DisplayName = RankingLeaderboardService.PublicName(p.Nickname, p.FullName),
                    PhotoUrl = p.PhotoUrl,
                });
            }
            else
            {
                result.Seats.Add(new MyTableSeatResponse { SeatNumber = seat });
            }
        }
        return result;
    }

    /// <summary>
    /// Bolha: os prêmios confirmados (TournamentPrizes) ou, sem confirmação, a sugestão da aba Premiação
    /// (<see cref="PrizeService.CalculatePrizesAsync"/>) — líquido ≤ 0 ou menos de 2 premiados = sem bolha.
    /// </summary>
    private async Task<MyBubbleResponse?> LoadBubbleAsync(TournamentInfo t, EntryRow latest, CancellationToken ct)
    {
        List<MyPrizePositionResponse> prizes;
        if (t.PrizeConfirmed)
        {
            prizes = await _db.TournamentPrizes.AsNoTracking()
                .Where(p => p.TournamentId == t.Id)
                .OrderBy(p => p.Position)
                .Select(p => new MyPrizePositionResponse { Position = p.Position, Amount = p.Amount })
                .ToListAsync(ct);
        }
        else
        {
            try
            {
                var calc = await _prizes.CalculatePrizesAsync(t.Id, null, ct);
                prizes = calc.Prizes.Select(p => new MyPrizePositionResponse { Position = p.Position, Amount = p.Amount }).ToList();
            }
            catch (DomainException)
            {
                prizes = [];
            }
        }
        if (prizes.Count == 0)
            return null;

        var remaining = Math.Max(0, t.PlayersRemaining);
        return new MyBubbleResponse
        {
            Confirmed = t.PrizeConfirmed,
            PrizeCount = prizes.Count,
            PlayersRemaining = remaining,
            PlacesToMoney = Math.Max(0, remaining - prizes.Count),
            // Quem já saiu com posição premiada também está "na premiação"
            InTheMoney = (latest.FinalPosition is { } pos && pos <= prizes.Count)
                || (latest.FinalPosition is null && remaining > 0 && remaining <= prizes.Count),
            Prizes = prizes,
        };
    }

    /// <summary>
    /// "Você está no tempo": o último uso de timebank do torneio ainda dentro dos segundos (não desfeito) é de uma
    /// inscrição da conta. O dealer registra o uso no começo de cada timebank (o celular dele conta o tempo), então é o
    /// melhor sinal que existe — não há estado "cronômetro em andamento" no servidor.
    /// </summary>
    private async Task<DateTimeOffset?> OnTheClockUntilAsync(Guid tournamentId, int seconds, HashSet<Guid> myEntryIds, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddSeconds(-seconds);
        var usedType = TimebankService.UsedType;
        var last = await _db.TournamentActivities.AsNoTracking()
            .Where(a => a.TournamentId == tournamentId && a.Type == usedType && a.UndoneAt == null && a.CreatedAt >= since)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.EntryId, a.CreatedAt })
            .FirstOrDefaultAsync(ct);
        if (last?.EntryId is { } entryId && myEntryIds.Contains(entryId))
            return last.CreatedAt.AddSeconds(seconds);
        return null;
    }

    /// <summary>
    /// Quem responde pelo torneio: quem o criou (se tem WhatsApp), senão o dono do clube. Número só com dígitos e DDI.
    /// Só contas que ligaram "Mostrar meu WhatsApp aos jogadores" (<c>User.ShowWhatsappToPlayers</c>, opt-in da
    /// SPEC-20261108-1636 pedido pela revisão independente da onda 1): sem ninguém com a opção ligada, o botão
    /// "Chamar o organizador" não aparece.
    /// </summary>
    private async Task<(string? Whatsapp, string? Name)> OrganizerAsync(TournamentInfo t, CancellationToken ct)
    {
        var ids = new List<Guid> { t.OwnerId };
        if (t.CreatedBy is { } creator && creator != t.OwnerId)
            ids.Insert(0, creator);
        var users = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.IsActive && u.ShowWhatsappToPlayers)
            .Select(u => new { u.Id, u.FullName, u.Nickname, u.Whatsapp })
            .ToListAsync(ct);
        foreach (var id in ids)
        {
            var u = users.FirstOrDefault(x => x.Id == id);
            var digits = WhatsappDigits(u?.Whatsapp);
            if (u is not null && digits is not null)
                return (digits, string.IsNullOrWhiteSpace(u.Nickname) ? u.FullName : u.Nickname);
        }
        return (null, null);
    }

    /// <summary>Só dígitos; número brasileiro sem DDI (10 ou 11 dígitos) ganha o 55. Null quando não dá para ligar.</summary>
    public static string? WhatsappDigits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length is 10 or 11)
            digits = "55" + digits;
        return digits.Length is >= 12 and <= 15 ? digits : null;
    }

    // ------------------------------------------------------------------ apoio

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
    private static string LabelFor(TournamentInfo t, string ownerPlan, Guid userId) =>
        LabelFor(t.HomeGameIsPrimary, ownerPlan, t.HomeGameName, t.OwnerId, t.OwnerName, userId);

    private static string LabelFor(bool isPrimary, string ownerPlan, string homeGameName, Guid ownerId, string? ownerName, Guid userId)
    {
        var display = HomeGamePrimary.DisplayNameFor(isPrimary, ownerPlan, homeGameName);
        if (display is not null)
            return display;
        if (ownerId == userId)
            return OwnLabel;
        var owner = ownerName?.Trim();
        return string.IsNullOrEmpty(owner) ? homeGameName : $"Torneios de {owner}";
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
