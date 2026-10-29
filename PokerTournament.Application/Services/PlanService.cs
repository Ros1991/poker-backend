using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Planos Mesa/Clube/Liga (SPEC-20261023-1205). O plano é da CONTA; o que vale num home game (e nos
/// torneios dele) é o plano efetivo do DONO (HomeGame.OwnerId) — um membro com conta Mesa usa tudo dentro
/// de um clube Clube. Plano efetivo = Plan se PlanExpiresAt for nulo ou futuro, senão Mesa (na leitura).
///
/// Só bloqueia CRIAÇÃO de recurso (home game, torneio, membro) e recursos exclusivos (mesas, premiação,
/// pagamentos, custos, ranking, dealer por QR). Invariante: nenhuma ação de torneio já criado (inscrição,
/// rebuy, add-on, eliminação, desfazer, timer, status, edição, exclusão, histórico) passa por aqui.
/// Queda de plano nunca apaga nada.
///
/// Toda recusa é <see cref="PlanLimitException"/> (HTTP 402). Todas as checagens retornam cedo só com
/// <c>Plans:Enforce = false</c> (chave de emergência; <c>Plans__Enforce=false</c> no ambiente).
/// SPEC-20261026-1126: o Admin do sistema segue o plano como qualquer conta — não existe mais exceção de plano
/// (no clube dele vale o plano dele; no clube de outra pessoa, o plano do dono). Para criar mais home games, o
/// Admin muda o próprio plano na Administração.
/// </summary>
public class PlanService
{
    public const string EnforceKey = "Plans:Enforce";
    /// <summary>402 tournaments_per_week para quem não é o dono do home game (sem uso nem horário do dono).</summary>
    public const string NotOwnerTournamentsPerWeekMessage =
        "O plano do dono deste home game atingiu o limite de torneios da semana.";

    // Plano por conta (dono): cache curto; ChangePlanAsync invalida na hora
    private static readonly TimeSpan PlanCacheTtl = TimeSpan.FromSeconds(30);
    // Dono do home game e home game do torneio não mudam: cache mais longo
    private static readonly TimeSpan OwnerCacheTtl = TimeSpan.FromMinutes(10);
    // Espaço próprio das travas consultivas dos limites (pg_advisory_xact_lock(int, int))
    private const int LockSpace = 1205;

    private readonly IAppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;

    public PlanService(IAppDbContext db, IMemoryCache cache, IConfiguration config)
    {
        _db = db;
        _cache = cache;
        _config = config;
    }

    /// <summary>Checagens ligadas? Só um "false" explícito em Plans:Enforce desliga (default true).</summary>
    public bool Enforce => !(bool.TryParse(_config[EnforceKey], out var on) && !on);

    private sealed record PlanState(string Plan, DateTimeOffset? ExpiresAt);

    // ------------------------------------------------------------------ leitura do plano

    /// <summary>Plano efetivo da conta (cache de 30 s por conta). Conta inexistente vale Mesa.</summary>
    public async Task<string> GetUserEffectivePlanAsync(Guid userId, CancellationToken ct = default)
    {
        var state = await _cache.GetOrCreateAsync(UserKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlanCacheTtl;
            return await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new PlanState(u.Plan, u.PlanExpiresAt))
                .FirstOrDefaultAsync(ct);
        });
        // O vencimento é conferido a cada leitura (o cache guarda o gravado, não o efetivo)
        return state is null ? PlanCatalog.Mesa : PlanCatalog.EffectivePlan(state.Plan, state.ExpiresAt);
    }

    /// <summary>Dono do home game (inclusive home game excluído — ele continua tendo dono). Null = não existe.</summary>
    public async Task<Guid?> GetHomeGameOwnerAsync(Guid homeGameId, CancellationToken ct = default)
    {
        var key = $"plan:hg-owner:{homeGameId}";
        if (_cache.TryGetValue(key, out Guid cached))
            return cached;
        var owner = await _db.HomeGames.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.Id == homeGameId)
            .Select(h => (Guid?)h.OwnerId)
            .FirstOrDefaultAsync(ct);
        if (owner is { } found)
            _cache.Set(key, found, OwnerCacheTtl);
        return owner;
    }

    /// <summary>Home game do torneio (inclusive torneio excluído). Null = não existe.</summary>
    public async Task<Guid?> GetTournamentHomeGameAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var key = $"plan:t-hg:{tournamentId}";
        if (_cache.TryGetValue(key, out Guid cached))
            return cached;
        var homeGame = await _db.Tournaments.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == tournamentId)
            .Select(t => (Guid?)t.HomeGameId)
            .FirstOrDefaultAsync(ct);
        if (homeGame is { } found)
            _cache.Set(key, found, OwnerCacheTtl);
        return homeGame;
    }

    /// <summary>Plano efetivo do dono do home game. Null = home game inexistente.</summary>
    public async Task<string?> GetOwnerPlanForHomeGameAsync(Guid homeGameId, CancellationToken ct = default)
    {
        var owner = await GetHomeGameOwnerAsync(homeGameId, ct);
        return owner is null ? null : await GetUserEffectivePlanAsync(owner.Value, ct);
    }

    /// <summary>Plano efetivo do dono do home game do torneio. Null = torneio inexistente.</summary>
    public async Task<string?> GetOwnerPlanForTournamentAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var homeGameId = await GetTournamentHomeGameAsync(tournamentId, ct);
        return homeGameId is null ? null : await GetOwnerPlanForHomeGameAsync(homeGameId.Value, ct);
    }

    /// <summary>Recursos que valem para o plano efetivo (com Plans:Enforce = false, todos).</summary>
    public IReadOnlyList<string> FeaturesFor(string effectivePlan) =>
        Enforce ? PlanCatalog.Get(effectivePlan).Features : PlanCatalog.AllFeatures;

    /// <summary>O plano efetivo libera o recurso? (com Plans:Enforce = false, sempre)</summary>
    public bool Allows(string effectivePlan, PlanFeature feature) =>
        !Enforce || PlanCatalog.Get(effectivePlan).Has(feature);

    /// <summary>O dono do home game tem o recurso? Home game inexistente responde true (quem chamou dá 404).</summary>
    public async Task<bool> HasFeatureForHomeGameAsync(Guid homeGameId, PlanFeature feature, CancellationToken ct = default)
    {
        if (!Enforce)
            return true;
        var plan = await GetOwnerPlanForHomeGameAsync(homeGameId, ct);
        return plan is null || PlanCatalog.Get(plan).Has(feature);
    }

    /// <summary>O dono do torneio tem o recurso? Torneio inexistente responde true (quem chamou dá 404).</summary>
    public async Task<bool> HasFeatureForTournamentAsync(Guid tournamentId, PlanFeature feature, CancellationToken ct = default)
    {
        if (!Enforce)
            return true;
        var plan = await GetOwnerPlanForTournamentAsync(tournamentId, ct);
        return plan is null || PlanCatalog.Get(plan).Has(feature);
    }

    /// <summary>Preenche ownerPlan/planFeatures do torneio (plano do dono do home game dele).</summary>
    public async Task ApplyOwnerPlanAsync(TournamentResponse response, CancellationToken ct = default)
    {
        var plan = await GetOwnerPlanForHomeGameAsync(response.HomeGameId, ct) ?? PlanCatalog.Mesa;
        response.OwnerPlan = plan;
        response.PlanFeatures = [.. FeaturesFor(plan)];
    }

    /// <summary>Preenche ownerPlan/planFeatures dos home games (uma consulta por dono, cacheada).</summary>
    public async Task ApplyOwnerPlanAsync(IEnumerable<HomeGameResponse> responses, CancellationToken ct = default)
    {
        var byOwner = new Dictionary<Guid, string>();
        foreach (var response in responses)
        {
            if (!byOwner.TryGetValue(response.OwnerId, out var plan))
            {
                plan = await GetUserEffectivePlanAsync(response.OwnerId, ct);
                byOwner[response.OwnerId] = plan;
            }
            response.OwnerPlan = plan;
            response.PlanFeatures = [.. FeaturesFor(plan)];
        }
    }

    // ------------------------------------------------------------------ recursos exclusivos

    /// <summary>
    /// Recurso exclusivo no home game: sem ele → 402 feature_locked. Home game inexistente passa
    /// (o controller responde 404). <paramref name="message"/> troca a mensagem padrão.
    /// </summary>
    public async Task EnsureFeatureForHomeGameAsync(
        Guid homeGameId, PlanFeature feature, string? message = null, CancellationToken ct = default)
    {
        if (!Enforce)
            return;
        var plan = await GetOwnerPlanForHomeGameAsync(homeGameId, ct);
        if (plan is not null && !PlanCatalog.Get(plan).Has(feature))
            throw Locked(plan, feature, message);
    }

    /// <summary>Recurso exclusivo no torneio (plano do dono do home game dele): sem ele → 402 feature_locked.</summary>
    public async Task EnsureFeatureForTournamentAsync(
        Guid tournamentId, PlanFeature feature, string? message = null, CancellationToken ct = default)
    {
        if (!Enforce)
            return;
        var plan = await GetOwnerPlanForTournamentAsync(tournamentId, ct);
        if (plan is not null && !PlanCatalog.Get(plan).Has(feature))
            throw Locked(plan, feature, message);
    }

    private static PlanLimitException Locked(string currentPlan, PlanFeature feature, string? message) =>
        new(PlanLimitException.FeatureLocked,
            message ?? PlanCatalog.FeatureLockedMessage(feature),
            currentPlan,
            PlanCatalog.MinimumPlanFor(feature),
            feature: feature.ToString());

    // ------------------------------------------------------------------ limites de criação

    /// <summary>
    /// Novo home game como dono (SPEC-20261029-1723: o principal da conta não conta no limite). Dentro de uma transação,
    /// trava por dono (dois cliques simultâneos não passam juntos).
    /// <list type="bullet">
    /// <item>Conta SEM principal (só conta antiga sem backfill ou confirmada por fora do fluxo, como os scripts de teste
    /// que confirmam por SQL): o novo home game vira o principal e passa em qualquer plano — devolve true.</item>
    /// <item>Com principal: os outros home games da conta (o filtro global já tira os excluídos) contra
    /// <c>MaxOwnedHomeGames - 1</c> → Mesa e Clube nenhum (402 home_games_limit, "a sua conta já é o seu clube"), Liga 2
    /// (o 3º home game passa, o 4º dá 402). Devolve false.</item>
    /// </list>
    /// Com Plans:Enforce = false só a regra do principal vale (nunca 402).
    /// </summary>
    public async Task<bool> EnsureCanCreateHomeGameAsync(Guid userId, CancellationToken ct = default)
    {
        await LockAsync($"owner:{userId}", ct);
        if (!await _db.HomeGames.AnyAsync(h => h.OwnerId == userId && h.IsPrimary, ct))
            return true;
        if (!Enforce)
            return false;

        var plan = await GetUserEffectivePlanAsync(userId, ct);
        var definition = PlanCatalog.Get(plan);
        var others = await _db.HomeGames.CountAsync(h => h.OwnerId == userId && !h.IsPrimary, ct);
        if (others < definition.MaxOwnedHomeGames - 1)
            return false;

        var owned = others + 1;
        var next = NextPlan(plan, p => p.MaxOwnedHomeGames > owned);
        if (definition.MaxOwnedHomeGames <= 1)
        {
            // Mesa e Clube: a conta tem um só (o principal), que é a própria conta — não há "outro home game" para criar.
            // Sem usage/limit: "1 de 1 home game" não faz sentido para quem nem vê home game.
            var single = $"No plano {plan} a sua conta já é o seu clube: não dá para criar outros home games.";
            if (next is not null)
                single += $" No plano {next.Name} você pode ter até {Count(next.MaxOwnedHomeGames, "home game", "home games")}"
                    + $" (o da sua conta e mais {next.MaxOwnedHomeGames - 1}).";
            throw new PlanLimitException(
                PlanLimitException.HomeGamesLimit, single, plan, next?.Name ?? PlanCatalog.Liga);
        }

        var message = $"O plano {plan} permite até {Count(definition.MaxOwnedHomeGames, "home game", "home games")} como dono"
            + $" (o da sua conta e mais {definition.MaxOwnedHomeGames - 1}).";
        if (next is not null)
            message += $" No plano {next.Name} você pode ter até {Count(next.MaxOwnedHomeGames, "home game", "home games")}.";

        throw new PlanLimitException(
            PlanLimitException.HomeGamesLimit, message, plan, next?.Name ?? PlanCatalog.Liga,
            usage: owned, limit: definition.MaxOwnedHomeGames);
    }

    /// <summary>
    /// Novo torneio no home game (plano do dono): com TournamentsPer7Days definido (Mesa: 1), conta os
    /// torneios criados pelos home games do dono nos últimos 7 dias — cancelado e excluído não contam, o finalizado
    /// conta sempre (SPEC-20261026-1907, ver TournamentsInWindowAsync) — e lança 402
    /// tournaments_per_week. Para o dono (<paramref name="callerId"/> = dono ou não informado) o 402 traz usage e
    /// nextAllowedAt; para quem não é o dono (membro administrador) sai sem usage/nextAllowedAt e com uma mensagem
    /// genérica — a contagem e o horário são dos home games do dono, inclusive os que o membro não enxerga
    /// (o detalhe fica no GET me/plan do dono).
    /// </summary>
    public async Task EnsureCanCreateTournamentAsync(
        Guid homeGameId, Guid? callerId = null, CancellationToken ct = default)
    {
        if (!Enforce)
            return;

        var owner = await GetHomeGameOwnerAsync(homeGameId, ct);
        if (owner is null)
            return; // o serviço responde "Home game não encontrado."
        var plan = await GetUserEffectivePlanAsync(owner.Value, ct);
        var limit = PlanCatalog.Get(plan).TournamentsPer7Days;
        if (limit is null)
            return;

        await LockAsync($"owner:{owner.Value}", ct);
        var window = await TournamentsInWindowAsync(owner.Value, ct);
        var nextAllowedAt = NextTournamentAllowedAt(window, limit.Value);
        if (nextAllowedAt is null)
            return;

        var next = NextPlan(plan, p => p.TournamentsPer7Days is null || p.TournamentsPer7Days > window.Count);
        if (callerId is { } caller && caller != owner.Value)
            throw new PlanLimitException(
                PlanLimitException.TournamentsPerWeek, NotOwnerTournamentsPerWeekMessage, plan,
                next?.Name ?? PlanCatalog.Clube, limit: limit.Value);

        var message =
            $"O plano {plan} permite criar {Count(limit.Value, "torneio", "torneios")} a cada 7 dias. " +
            $"O próximo libera {Until(nextAllowedAt.Value)}.";
        if (next is not null)
            message += next.TournamentsPer7Days is null
                ? $" No plano {next.Name} os torneios são ilimitados."
                : $" No plano {next.Name} são {Count(next.TournamentsPer7Days.Value, "torneio", "torneios")} a cada 7 dias.";

        throw new PlanLimitException(
            PlanLimitException.TournamentsPerWeek, message, plan, next?.Name ?? PlanCatalog.Clube,
            usage: window.Count, limit: limit.Value, nextAllowedAt: nextAllowedAt);
    }

    /// <summary>
    /// Novo membro no home game (plano do dono): conta os membros (além do dono) contra
    /// MaxMembersPerHomeGame → 402 members_limit. Dentro de uma transação, trava por home game.
    /// </summary>
    public async Task EnsureCanAddMemberAsync(Guid homeGameId, CancellationToken ct = default)
    {
        if (!Enforce)
            return;

        var owner = await GetHomeGameOwnerAsync(homeGameId, ct);
        if (owner is null)
            return;
        var plan = await GetUserEffectivePlanAsync(owner.Value, ct);
        var max = PlanCatalog.Get(plan).MaxMembersPerHomeGame;
        if (max is null)
            return;

        await LockAsync($"members:{homeGameId}", ct);
        var used = await _db.HomeGameMembers.CountAsync(m => m.HomeGameId == homeGameId, ct);
        if (used < max.Value)
            return;

        var next = NextPlan(plan, p => p.MaxMembersPerHomeGame is null || p.MaxMembersPerHomeGame > used);
        var message = max.Value == 0
            ? $"O plano {plan} não permite adicionar membros ao home game."
            : $"O plano {plan} permite até {Count(max.Value, "membro", "membros")} além do dono.";
        if (next is not null)
            message += next.MaxMembersPerHomeGame is null
                ? $" No plano {next.Name} os membros são ilimitados."
                : $" No plano {next.Name} são até {Count(next.MaxMembersPerHomeGame.Value, "membro", "membros")} além do dono.";

        throw new PlanLimitException(
            PlanLimitException.MembersLimit, message, plan, next?.Name ?? PlanCatalog.Liga,
            usage: used, limit: max.Value);
    }

    // ------------------------------------------------------------------ Meu plano

    /// <summary>
    /// Plano da conta logada: gravado, efetivo, vencimento, origem, nota, limites e recursos do plano
    /// efetivo, uso (home games, torneios nos últimos 7 dias + quando libera o próximo, membros por
    /// home game) e o catálogo. Null = conta inexistente. Com Plans:Enforce = false, nextAllowedAt sai nulo:
    /// nada vai bloquear a criação. O Admin do sistema recebe os mesmos limites de qualquer conta (SPEC-20261026-1126).
    /// </summary>
    public async Task<MyPlanResponse?> GetMyPlanAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return null;

        var effective = PlanCatalog.EffectivePlan(user);
        var definition = PlanCatalog.Get(effective);
        var limitsApply = Enforce;

        // O principal primeiro (SPEC-20261029-1723), depois os outros por nome
        var homeGames = await _db.HomeGames.AsNoTracking()
            .Where(h => h.OwnerId == userId)
            .OrderByDescending(h => h.IsPrimary)
            .ThenBy(h => h.Name)
            .Select(h => new { h.Id, h.Name, h.IsPrimary, Members = h.Members.Count })
            .ToListAsync(ct);

        var window = await TournamentsInWindowAsync(userId, ct);
        var nextAllowedAt = limitsApply && definition.TournamentsPer7Days is { } perWeek
            ? NextTournamentAllowedAt(window, perWeek)
            : null;

        return new MyPlanResponse
        {
            Plan = PlanCatalog.Normalize(user.Plan) ?? PlanCatalog.Mesa,
            EffectivePlan = effective,
            ExpiresAt = user.PlanExpiresAt,
            Source = user.PlanSource,
            Note = user.PlanNote,
            PrimaryHomeGameId = homeGames.FirstOrDefault(h => h.IsPrimary)?.Id,
            Limits = new PlanLimitsResponse
            {
                MaxOwnedHomeGames = definition.MaxOwnedHomeGames,
                HomeGamesVisible = definition.HomeGamesVisible,
                MaxMembersPerHomeGame = definition.MaxMembersPerHomeGame,
                TournamentsPer7Days = definition.TournamentsPer7Days,
            },
            Features = [.. definition.Features],
            Usage = new PlanUsageResponse
            {
                HomeGames = new PlanUsageCounter { Used = homeGames.Count, Limit = definition.MaxOwnedHomeGames },
                TournamentsLast7Days = new PlanTournamentUsage
                {
                    Used = window.Count,
                    Limit = definition.TournamentsPer7Days,
                    NextAllowedAt = nextAllowedAt,
                },
                NextTournamentAllowedAt = nextAllowedAt,
                MembersByHomeGame = homeGames
                    .Select(h => new PlanMemberUsage
                    {
                        HomeGameId = h.Id,
                        Name = h.Name,
                        IsPrimary = h.IsPrimary,
                        Used = h.Members,
                        Limit = definition.MaxMembersPerHomeGame,
                    })
                    .ToList(),
            },
            Catalog = PlanCatalog.All,
        };
    }

    // ------------------------------------------------------------------ troca de plano (suporte)

    /// <summary>
    /// Troca plano/vencimento/origem/nota da conta e grava AuditLog { EntityType = "User",
    /// Action = "PlanChange" } com os valores antigos e novos. Invalida o cache do plano do dono na hora.
    /// Plano ou origem fora do catálogo → 400. Null = conta inexistente.
    /// Usado pelo PUT users/{id}/plan (Admin) e pela tela de administração (SPEC-20261023-1206).
    /// </summary>
    public async Task<User?> ChangePlanAsync(
        Guid userId, string? plan, DateTimeOffset? expiresAt, string? source, string? note, Guid? byUserId,
        CancellationToken ct = default)
    {
        var canonicalPlan = PlanCatalog.Normalize(plan)
            ?? throw new DomainException("Plano inválido. Use Mesa, Clube ou Liga.");
        var canonicalSource = string.IsNullOrWhiteSpace(source)
            ? nameof(PlanSource.Manual)
            : Enum.GetNames<PlanSource>().FirstOrDefault(n => string.Equals(n, source.Trim(), StringComparison.OrdinalIgnoreCase))
              ?? throw new DomainException("Origem do plano inválida. Use Manual, Cortesia, Trial ou Assinatura.");
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > 200 })
            throw new DomainException("A observação do plano deve ter no máximo 200 caracteres.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return null;

        var oldValues = new Dictionary<string, object?>
        {
            ["Plan"] = user.Plan,
            ["PlanExpiresAt"] = user.PlanExpiresAt,
            ["PlanSource"] = user.PlanSource,
            ["PlanNote"] = user.PlanNote,
        };
        var newValues = new Dictionary<string, object?>
        {
            ["Plan"] = canonicalPlan,
            ["PlanExpiresAt"] = expiresAt,
            ["PlanSource"] = canonicalSource,
            ["PlanNote"] = trimmedNote,
        };
        var changed = newValues.Keys.Where(k => !Equals(oldValues[k], newValues[k])).ToList();

        var now = DateTimeOffset.UtcNow;
        user.Plan = canonicalPlan;
        user.PlanExpiresAt = expiresAt;
        user.PlanSource = canonicalSource;
        user.PlanNote = trimmedNote;
        user.PlanUpdatedAt = now;
        user.PlanUpdatedBy = byUserId;

        var byName = byUserId is null
            ? null
            : await _db.Users.AsNoTracking().Where(u => u.Id == byUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct);

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = "User",
            EntityId = user.Id,
            Action = "PlanChange",
            OldValues = JsonSerializer.Serialize(oldValues),
            NewValues = JsonSerializer.Serialize(newValues),
            ChangedFields = JsonSerializer.Serialize(changed),
            UserId = byUserId,
            UserName = byName,
            CreatedAt = now,
        });

        await _db.SaveChangesAsync(ct);
        // Depois de gravar: a próxima leitura (telão, filtro, respostas) já pega o plano novo
        _cache.Remove(UserKey(user.Id));
        return user;
    }

    // ------------------------------------------------------------------ apoio

    /// <summary>
    /// CreatedAt (crescente) dos torneios que contam na janela de 7 dias dos home games do dono (SPEC-20261026-1907):
    /// cancelado NÃO conta e excluído NÃO conta — salvo o torneio FINALIZADO, que conta sempre (inclusive excluído ou
    /// de home game excluído): torneio jogado até o fim não devolve a vaga, assim como não pode mais ser cancelado
    /// (TournamentService). Antes (SPEC-20261023-1205) cancelados e excluídos contavam.
    /// </summary>
    private async Task<List<DateTimeOffset>> TournamentsInWindowAsync(Guid ownerId, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow - PlanCatalog.TournamentWindow;
        var cancelled = nameof(TournamentStatus.Cancelled);
        var finished = nameof(TournamentStatus.Finished);
        // "Finalizado" = já foi finalizado alguma vez (FirstFinishedAt, nunca limpo): reaberto pelo desfazer eliminação e
        // depois cancelado ou excluído continua contando (revisão geral 2)
        return await _db.Tournaments.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.HomeGame.OwnerId == ownerId && t.CreatedAt >= since
                && (t.Status != cancelled || t.FirstFinishedAt != null)
                && (t.DeletedAt == null || t.Status == finished || t.FirstFinishedAt != null))
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Com <paramref name="limit"/> torneios por janela: null = ainda cabe um; senão, quando sai da
    /// janela o torneio que abre a vaga (o (n − limite + 1)-ésimo mais antigo).
    /// </summary>
    private static DateTimeOffset? NextTournamentAllowedAt(List<DateTimeOffset> window, int limit)
    {
        if (window.Count < limit)
            return null;
        return window[window.Count - limit] + PlanCatalog.TournamentWindow;
    }

    /// <summary>Plano mais barato ACIMA do atual que permite a ação.</summary>
    private static PlanDefinition? NextPlan(string currentPlan, Func<PlanDefinition, bool> allows)
    {
        var order = PlanCatalog.Get(currentPlan).Order;
        return PlanCatalog.All.OrderBy(p => p.Order).FirstOrDefault(p => p.Order > order && allows(p));
    }

    // Trava consultiva dentro da transação de quem chamou (sem transação ela não seguraria nada)
    private async Task LockAsync(string key, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is null)
            return;
        await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockSpace}, hashtext({key}))", ct);
    }

    private static string Count(int n, string singular, string plural) => n == 1 ? $"1 {singular}" : $"{n} {plural}";

    /// <summary>"em 3 dias", "em 5 horas", "em menos de 1 hora".</summary>
    private static string Until(DateTimeOffset when)
    {
        var left = when - DateTimeOffset.UtcNow;
        if (left.TotalHours < 1)
            return "em menos de 1 hora";
        if (left.TotalHours < 24)
        {
            var hours = (int)Math.Ceiling(left.TotalHours);
            return hours == 1 ? "em 1 hora" : $"em {hours} horas";
        }
        var days = (int)Math.Ceiling(left.TotalDays);
        return days == 1 ? "em 1 dia" : $"em {days} dias";
    }

    private static string UserKey(Guid userId) => $"plan:user:{userId}";
}
