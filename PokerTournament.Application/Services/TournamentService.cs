using System.Globalization;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PokerTournament.Application.Addons;
using PokerTournament.Application.Timebank;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Knockout;
using PokerTournament.Application.Pix;
using PokerTournament.Application.Plans;
using PokerTournament.Application.Publicity;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

public class TournamentService
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;
    private readonly PlanService _plans;
    private readonly TournamentNotifier _notifier;
    private readonly JoinRequestService _joinRequests;
    private readonly IMemoryCache _cache;

    /// <summary>Quantidade de dealers aceita na criação (CP17 da SPEC-20261023-1215).</summary>
    public const int MaxDealerCount = 20;

    // Valores em pt-BR sem depender de ICU/cultura do servidor: R$ 1.234,56 (mesmo formato do TournamentHistoryService)
    private static readonly NumberFormatInfo PtBrNumber = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberDecimalDigits = 2
    };

    public TournamentService(
        IAppDbContext db, IMapper mapper, PlanService plans, TournamentNotifier notifier, JoinRequestService joinRequests, IMemoryCache cache)
    {
        _db = db;
        _mapper = mapper;
        _plans = plans;
        _notifier = notifier;
        _joinRequests = joinRequests;
        _cache = cache;
    }

    /// <summary>
    /// A página pública montada (<c>PublicTournamentsController</c>, cache de 10 s) cai na hora que a divulgação, o status
    /// ou a existência do torneio mudam: despublicar, cancelar ou excluir dá 404 já na próxima leitura (revisão da 1639).
    /// Chamar DEPOIS do SaveChanges/commit (antes, uma leitura no meio recolocaria a página velha no cache).
    /// </summary>
    private void ForgetPublicPage(Tournament tournament)
    {
        if (!string.IsNullOrEmpty(tournament.PublicSlug))
            _cache.Remove(PublicTournamentService.CacheKey(tournament.PublicSlug));
    }

    /// <summary>
    /// Os quatro campos de divulgação só valem para quem GERENCIA o home game (dono, membro administrador ou Admin do
    /// sistema): no PUT do torneio e na criação por membro comum eles são ignorados, sem erro (revisão da 1639 — antes
    /// qualquer membro tornava o torneio público e ligava o WhatsApp do organizador).
    /// </summary>
    private async Task<bool> ManagesAsync(Guid homeGameId, Guid? userId, bool isAdmin, CancellationToken ct) =>
        userId is { } uid && await HomeGameAccess.CanManageAsync(_db, homeGameId, uid, isAdmin, ct);

    /// <summary>
    /// Nome do clube que entra no slug público (<c>/t/clube-do-ze-sexta-2026-11-21</c>): o rótulo público do home game —
    /// o nome dele, ou "Mesa de &lt;nome público do dono&gt;" no principal sem nome próprio. O nome completo do dono nunca
    /// entra na URL pública (revisão da 1639, achado 4: o padrão "Clube de &lt;nome completo&gt;" virava slug).
    /// </summary>
    private async Task<string> SlugClubNameAsync(Guid homeGameId, CancellationToken ct)
    {
        var hg = await _db.HomeGames.AsNoTracking()
            .Where(h => h.Id == homeGameId)
            .Select(h => new { h.Name, h.IsPrimary, h.OwnerId, OwnerName = h.Owner.FullName, OwnerNickname = h.Owner.Nickname })
            .FirstOrDefaultAsync(ct);
        if (hg is null)
            return string.Empty;
        var plan = await _plans.GetUserEffectivePlanAsync(hg.OwnerId, ct);
        return HomeGamePrimary.PublicLabelFor(hg.IsPrimary, plan, hg.Name, hg.OwnerNickname, hg.OwnerName);
    }

    /// <summary>
    /// Vagas subiram ou o limite saiu: quem couber na lista de espera sobe, em ordem (com <c>JoinRequestPromoted</c>). Exige
    /// a transação com a trava do torneio já aberta; o <paramref name="newMax"/> pode ainda não estar gravado.
    /// </summary>
    private async Task PromoteWaitlistIfRoomAsync(Tournament tournament, int? oldMax, int? newMax, CancellationToken ct)
    {
        if (newMax == oldMax)
            return;
        if (newMax is null || (oldMax is { } old && newMax > old))
            await _joinRequests.PromoteWaitlistAsync(tournament.Id, newMax, freed: 0, ct);
    }

    /// <summary>
    /// Map + plano do dono (ownerPlan/planFeatures, SPEC-20261023-1205): é por ele que o frontend
    /// decide os cadeados do torneio e do terminal do dealer.
    /// </summary>
    private async Task<TournamentResponse> ToResponseAsync(Tournament tournament, CancellationToken ct)
    {
        var response = _mapper.Map<TournamentResponse>(tournament);
        await _plans.ApplyOwnerPlanAsync(response, ct);
        return response;
    }

    public async Task<PaginatedResponse<TournamentResponse>> GetAllAsync(
        Guid homeGameId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        // Página/tamanho fora da faixa (page 0, negativa ou enorme; pageSize ≤ 0) não chegam ao OFFSET/LIMIT:
        // antes o PostgreSQL respondia 2201X/2201W e a API dava 500. Além do fim = lista vazia com 200.
        var paging = Paging.Normalize(page, pageSize);
        var query = _db.Tournaments
            .Include(t => t.HomeGame)
            .Include(t => t.Ranking)
            .Where(t => t.HomeGameId == homeGameId && t.Status != nameof(TournamentStatus.Cancelled))
            .OrderByDescending(t => t.Date)
            .ThenBy(t => t.Id);

        var totalCount = await query.CountAsync(ct);
        if (paging.IsPastEnd(totalCount))
            return new PaginatedResponse<TournamentResponse>([], paging.Page, paging.PageSize, totalCount);

        var items = await query
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(ct);

        var mapped = _mapper.Map<List<TournamentResponse>>(items);
        // Todos do mesmo home game: uma leitura de plano (cacheada) por página
        foreach (var item in mapped)
            await _plans.ApplyOwnerPlanAsync(item, ct);
        return new PaginatedResponse<TournamentResponse>(mapped, paging.Page, paging.PageSize, totalCount);
    }

    /// <summary>
    /// Rotas aninhadas /home-games/{homeGameId}/tournaments/{id}: o TenantAccessFilter só confere
    /// o {homeGameId} da rota, então o torneio precisa ser DESSE home game. Torneio de outro home
    /// game responde igual ao inexistente (mesma mensagem), sem revelar que ele existe.
    /// </summary>
    public async Task EnsureInHomeGameAsync(Guid homeGameId, Guid id, CancellationToken ct = default)
    {
        var belongs = await _db.Tournaments
            .AnyAsync(t => t.Id == id && t.HomeGameId == homeGameId, ct);
        if (!belongs)
            throw new DomainException("Torneio não encontrado.");
    }

    public async Task<TournamentResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.HomeGame)
            .Include(t => t.Ranking)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        return await ToResponseAsync(tournament, ct);
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

        return await ToResponseAsync(tournament, ct);
    }

    /// <summary>
    /// Atualiza a chave PIX do dia. Editável em qualquer status exceto Cancelled —
    /// ajuste operacional (a chave pode mudar durante o torneio), não config de Draft.
    /// PIX é financeiro: o PUT pix-key exige o recurso Payments (402 no Mesa, via [RequirePlanFeature]).
    /// SPEC-20261023-1218: grava a chave no formato do DICT (PixKeyNormalizer); fora de formato → 400 com a mensagem
    /// da tabela; 11 dígitos que podem ser CPF ou celular exigem <paramref name="pixKeyType"/>; vazio remove.
    /// </summary>
    public async Task<TournamentResponse> UpdatePixKeyAsync(
        Guid id, string? pixKey, string? pixKeyType = null, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.HomeGame)
            .Include(t => t.Ranking)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (tournament.Status == nameof(TournamentStatus.Cancelled))
            throw new DomainException("Torneio cancelado não pode ser alterado.");

        tournament.ResponsiblePixKey = PixKeyNormalizer.NormalizeForWrite(pixKey, pixKeyType);
        await _db.SaveChangesAsync(ct);

        return await ToResponseAsync(tournament, ct);
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
        // O link público dá 404 na hora, sem esperar o cache de 10 s
        ForgetPublicPage(tournament);
    }

    /// <summary>
    /// Cria o torneio. Limite do plano do dono (SPEC-20261023-1205): no Mesa, 1 torneio criado a cada
    /// 7 dias (402 tournaments_per_week com nextAllowedAt) — vale também para o Admin do sistema (SPEC-20261026-1126).
    /// Sem o recurso Payments (Mesa) o torneio nasce sem
    /// custos automáticos de staff/ranking, sem ranking e sem PIX do dia (ver StripPaidFields).
    /// Tudo numa transação com trava por dono: dois cliques simultâneos não criam dois torneios no Mesa.
    /// SPEC-20261023-1215: com a estrutura escolhida, o torneio já nasce com os custos padrão dela (CostType
    /// Default, Pending) e, se ela tem dealer pago, com "Dealers (N × R$ X)" (N = DealerCount, null = 1) — só se o
    /// dono tem o recurso CostExtras (no Mesa nada é copiado); TotalCosts/NetPrizePool na mesma gravação.
    /// SPEC-20261026-1909: formato knockout e bounty do pedido; sem formato no pedido, herda o padrão da estrutura
    /// (sem estrutura: Normal). Knockout vale em todos os planos (o Mesa não perde o formato).
    /// </summary>
    public async Task<TournamentResponse> CreateAsync(
        Guid homeGameId, CreateTournamentRequest request, Guid? createdBy = null, bool createdByIsAdmin = false, CancellationToken ct = default)
    {
        var homeGame = await _db.HomeGames
            .FirstOrDefaultAsync(h => h.Id == homeGameId, ct)
            ?? throw new DomainException("Home game não encontrado.");
        EnsureValidDealerCount(request.DealerCount);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Quem cria: o 402 de quem não é o dono sai sem o uso/horário dos home games do dono
        await _plans.EnsureCanCreateTournamentAsync(homeGameId, createdBy, ct);

        var tournament = _mapper.Map<Tournament>(request);
        tournament.HomeGameId = homeGameId;
        tournament.Status = nameof(TournamentStatus.Draft);
        tournament.CreatedBy = createdBy;
        tournament.CurrentLevel = 1;
        if (!await _plans.HasFeatureForHomeGameAsync(homeGameId, PlanFeature.Payments, ct))
            StripPaidFields(tournament);
        else
            // PIX do dia no formato do DICT (SPEC-20261023-1218). Só com Payments: no Mesa a chave já foi zerada e
            // não é validada (campo escondido não pode dar 400).
            tournament.ResponsiblePixKey = PixKeyNormalizer.NormalizeForWrite(
                request.ResponsiblePixKey, request.ResponsiblePixKeyType);
        // Custos padrão da estrutura (CP6/CP19): só com o recurso CostExtras do DONO (Mesa não recebe; com
        // Plans:Enforce = false, recebe). No catálogo CostExtras e Payments andam juntos.
        var copyDefaultCosts = await _plans.HasFeatureForHomeGameAsync(homeGameId, PlanFeature.CostExtras, ct);

        // Copy blind levels from template if specified
        BlindStructure? structure = null;
        if (request.BlindStructureId.HasValue)
        {
            structure = await _db.BlindStructures
                .Include(bs => bs.Levels)
                .Include(bs => bs.DefaultCosts)
                .AsSplitQuery()
                .FirstOrDefaultAsync(bs => bs.Id == request.BlindStructureId.Value, ct);
            if (structure != null)
            {
                // Isolamento: só template global (HomeGameId nulo) ou estrutura do próprio home game
                if (structure.HomeGameId.HasValue && structure.HomeGameId != homeGameId)
                    throw new DomainException("Estrutura de blinds não pertence a este home game.");

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

                // Custos padrão + dealer (CP3/CP17/CP18): foto do momento da criação (CP5)
                if (copyDefaultCosts)
                {
                    foreach (var cost in BuildDefaultCostExtras(structure, request.DealerCount ?? 1, createdBy))
                        tournament.CostExtras.Add(cost);
                }
            }
        }

        // Máximo de add-ons (SPEC-20261027-1311): pedido > padrão da estrutura > 1; 0 = ilimitado
        tournament.MaxAddons = AddonRules.Resolve(request.MaxAddons, structure?.DefaultMaxAddons ?? AddonRules.DefaultMax);

        // Timebank (SPEC-20261029-1722): pedido (0 = desligado) > padrão da estrutura > sem timebank; segundos 5–300 (30)
        (tournament.TimebankCount, tournament.TimebankSeconds) = TimebankRules.Resolve(
            request.TimebankCount, request.TimebankSeconds, structure?.DefaultTimebankCount, structure?.DefaultTimebankSeconds);

        // Knockout (SPEC-20261026-1909): pedido > padrão da estrutura > Normal; bounty validado contra buy-in e rebuy
        var structureKnockout = KnockoutRules.IsKnockout(structure?.DefaultKnockoutType);
        var koType = KnockoutRules.Parse(request.KnockoutType)
            ?? (structureKnockout ? structure!.DefaultKnockoutType : nameof(KnockoutType.None));
        var koBounty = request.BountyAmount ?? (structureKnockout ? structure!.DefaultBountyAmount : null);
        (tournament.KnockoutType, tournament.BountyAmount) =
            KnockoutRules.Resolve(koType, koBounty, tournament.BuyInAmount, tournament.RebuyAmount);
        tournament.TotalBounties = 0m;

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
                PixKeyType = PixKeyNormalizer.TypeOf(homeGame.PixKey),
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

        // Torneio novo: receita 0, staff e ranking 0 → só os custos padrão contam (líquido começa negativo
        // com custo fixo; o telão mostra 0 — DisplayService)
        tournament.TotalCosts = tournament.CostExtras.Sum(c => c.Amount);
        KnockoutRules.ApplyNet(tournament);

        // Divulgação (SPEC-20261108-1639): nasce privado; público já na criação ganha o slug da página /t/{slug}. Só quem
        // gerencia o home game divulga (membro comum: os campos são ignorados)
        if (await ManagesAsync(homeGameId, createdBy, createdByIsAdmin, ct) && TournamentPublicity.Apply(tournament, request))
            await TournamentPublicity.EnsureSlugAsync(_db, tournament, await SlugClubNameAsync(homeGameId, ct), ct);

        _db.Tournaments.Add(tournament);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await ToResponseAsync(tournament, ct);
    }

    /// <summary>
    /// Atalho "Divulgar" do painel (SPEC-20261108-1639): só a divulgação — público, descrição, vagas e a porta do WhatsApp
    /// do organizador. Vale em qualquer status menos cancelado (finalizado pode continuar público: a página diz "já
    /// aconteceu"). Ao ficar público pela primeira vez o slug nasce e nunca mais muda. Quem gerencia é conferido pelo
    /// controller (403). Subir ou tirar o limite de vagas promove quem couber na lista de espera, sob a trava do torneio;
    /// a página pública em cache cai na hora.
    /// </summary>
    public async Task<TournamentResponse> UpdatePublicityAsync(
        Guid id, UpdateTournamentPublicityRequest request, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await EntryService.LockSeatingAsync(_db, id, ct);

        var tournament = await _db.Tournaments
            .Include(t => t.HomeGame)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");
        if (tournament.Status == nameof(TournamentStatus.Cancelled))
            throw new DomainException(CancelledCannotBeEditedMessage);

        var oldMax = tournament.MaxPlayers;
        if (TournamentPublicity.Apply(tournament, request))
            await TournamentPublicity.EnsureSlugAsync(_db, tournament, await SlugClubNameAsync(tournament.HomeGameId, ct), ct);
        await PromoteWaitlistIfRoomAsync(tournament, oldMax, tournament.MaxPlayers, ct);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        ForgetPublicPage(tournament);
        await _notifier.PushPendingAsync(ct);
        return await ToResponseAsync(tournament, ct);
    }

    /// <summary>
    /// Mesa (sem o recurso Payments): o torneio NASCE sem custos automáticos nem PIX do dia —
    /// senão NetPrizePool divergiria de TotalPrizePool sem o organizador conseguir ver o motivo.
    /// Vale pelo plano do DONO (não de quem cria/edita); desligado com Plans:Enforce = false.
    /// Só na criação: a edição sem Payments mantém o que já estava gravado (ver UpdateAsync).
    /// </summary>
    private static void StripPaidFields(Tournament tournament)
    {
        tournament.StaffAmount = null;
        tournament.RankingContribMode = null;
        tournament.RankingContribValue = null;
        tournament.RankingId = null;
        tournament.ResponsiblePixKey = null;
    }

    public const string FinishedCannotBeEditedMessage = "Torneio finalizado não pode ser editado.";

    public const string CancelledCannotBeEditedMessage = "Torneio cancelado não pode ser editado.";

    public const string StructureLockedMessage =
        "A estrutura de blinds não muda depois que o torneio começa. Para mudar os níveis, use a aba Blinds do painel.";

    public const string KnockoutLockedMessage =
        "O formato do torneio (Normal, Knockout ou PKO) não muda depois que o torneio começa.";

    public const string PrizeConfirmedValuesMessage =
        "A premiação já foi confirmada com o líquido de agora. Para mudar buy-in, rebuy, add-on, staff, ranking ou bounty, " +
        "desfaça a confirmação na aba Premiação antes.";

    public const string RebuyValueRequiredMessage = "Há jogadores com rebuy: o valor do rebuy não pode ficar zerado.";

    public const string AddonValueRequiredMessage = "Há jogadores com add-on: o valor do add-on não pode ficar zerado.";

    /// <summary>Antes do começo (rascunho ou inscrições abertas): o relógio ligado aqui faz o torneio começar (SPEC-20261103-1927).</summary>
    public static bool IsBeforeStart(string status) =>
        status == nameof(TournamentStatus.Draft) || status == nameof(TournamentStatus.OpenForRegistration);

    /// <summary>
    /// Edição do torneio (SPEC-20261103-1927): vale do rascunho até o intervalo de acerto; finalizado e cancelado → 400.
    /// Depois de começar (Em andamento e intervalo de acerto, a regra do <see cref="IsBeforeStart"/>) a estrutura de blinds e
    /// o formato knockout ficam travados. Campo de staff, ranking ou PIX do dia ausente no corpo mantém o gravado. Mudar valores
    /// (buy-in, rebuy, add-on, staff, contribuição ao ranking, bounty) recalcula o devido de cada inscrição, os custos
    /// automáticos e os totais do torneio na mesma transação (<see cref="RecalcMoneyAsync"/>); com a premiação confirmada,
    /// mudar valor é recusado. Os pagamentos já registrados não mudam.
    /// </summary>
    public async Task<TournamentResponse> UpdateAsync(
        Guid id, CreateTournamentRequest request, Guid? updatedBy = null, bool updatedByIsAdmin = false, CancellationToken ct = default)
    {
        // Uma transação com a trava do torneio (a mesma da inscrição): inscrição simultânea espera o recálculo, e rebuy,
        // add-on ou pagamento simultâneos caem no xmin (409) em vez de deixar o total desencontrado
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await EntryService.LockSeatingAsync(_db, id, ct);

        var tournament = await _db.Tournaments
            .Include(t => t.BlindLevels)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (tournament.Status == nameof(TournamentStatus.Finished))
            throw new DomainException(FinishedCannotBeEditedMessage);
        if (tournament.Status == nameof(TournamentStatus.Cancelled))
            throw new DomainException(CancelledCannotBeEditedMessage);
        EnsureValidDealerCount(request.DealerCount);

        // Depois de começar: a estrutura (os níveis se editam na aba Blinds) e o formato knockout não mudam. "Começou" é a
        // mesma regra do relógio (IsBeforeStart): inscrições abertas ainda trocam estrutura e formato (revisão da 1927)
        var started = !IsBeforeStart(tournament.Status);
        var previousStructureId = tournament.BlindStructureId;
        if (started && request.BlindStructureId.HasValue && request.BlindStructureId != previousStructureId)
            throw new DomainException(StructureLockedMessage);
        if (started && KnockoutRules.Parse(request.KnockoutType) is { } requestedType && requestedType != tournament.KnockoutType)
            throw new DomainException(KnockoutLockedMessage);

        var before = MoneyValues.Of(tournament);
        // Queda de plano não apaga nada: sem Payments (Mesa) a edição mantém o que já estava gravado (staff,
        // ranking, contribuição e PIX do dia de um torneio criado no plano pago) e só ignora valores NOVOS da
        // requisição. Torneio criado no Mesa tem tudo nulo e continua nulo.
        var kept = (tournament.StaffAmount, tournament.RankingId, tournament.RankingContribMode,
            tournament.RankingContribValue, tournament.ResponsiblePixKey);
        var keptKnockout = (tournament.KnockoutType, tournament.BountyAmount);
        var keptTimebankCount = tournament.TimebankCount;
        _mapper.Map(request, tournament);
        // Começado: o pedido sem a estrutura não a desliga (o Map gravaria null)
        if (started)
            tournament.BlindStructureId = previousStructureId;
        // Máximo de add-ons (SPEC-20261027-1311): null = manter; o Map ignora o campo
        tournament.MaxAddons = AddonRules.Resolve(request.MaxAddons, tournament.MaxAddons);
        // Timebank (SPEC-20261029-1722): null = manter; 0 = desligar — o Map ignora os dois campos. No rascunho, mudar a
        // quantidade reaplica nas inscrições que já existem (cada uma volta a ter a quantidade nova; 0 ao desligar). Depois de
        // começar (SPEC-20261103-1927), cada inscrição ganha ou perde a diferença (quem já usou continua com o que usou a
        // menos), de 0 a 99; desligar zera.
        (tournament.TimebankCount, tournament.TimebankSeconds) = TimebankRules.Resolve(
            request.TimebankCount, request.TimebankSeconds, tournament.TimebankCount, tournament.TimebankSeconds);
        if (tournament.TimebankCount != keptTimebankCount)
        {
            var initial = TimebankRules.Initial(tournament);
            var delta = initial - (keptTimebankCount ?? 0);
            foreach (var entry in await _db.TournamentEntries.Where(e => e.TournamentId == tournament.Id).ToListAsync(ct))
                entry.TimebanksRemaining = !started || initial == 0
                    ? initial
                    : Math.Clamp(entry.TimebanksRemaining + delta, 0, TimebankRules.MaxRemaining);
        }
        var hasPayments = await _plans.HasFeatureForHomeGameAsync(tournament.HomeGameId, PlanFeature.Payments, ct);
        if (!hasPayments)
        {
            tournament.StaffAmount = kept.StaffAmount;
            tournament.RankingId = kept.RankingId;
            tournament.RankingContribMode = kept.RankingContribMode;
            tournament.RankingContribValue = kept.RankingContribValue;
            tournament.ResponsiblePixKey = kept.ResponsiblePixKey;
        }
        else
        {
            // Campo ausente no corpo = manter o gravado (revisão da SPEC-20261103-1927): o Map gravaria null e o recálculo
            // tiraria o staff ou o ranking de todos os jogadores. Quem manda o campo, com valor ou null, continua mandando.
            // O ranking é um grupo (vínculo, modo e valor da contribuição): qualquer um deles no corpo = os três do pedido,
            // como antes; nenhum = os três como estavam (o devido cobra a contribuição pelo modo e valor, com ou sem vínculo)
            if (!request.Sent.Contains(nameof(request.StaffAmount)))
                tournament.StaffAmount = kept.StaffAmount;
            if (!request.Sent.Contains(nameof(request.RankingId))
                && !request.Sent.Contains(nameof(request.RankingContribMode))
                && !request.Sent.Contains(nameof(request.RankingContribValue)))
            {
                tournament.RankingId = kept.RankingId;
                tournament.RankingContribMode = kept.RankingContribMode;
                tournament.RankingContribValue = kept.RankingContribValue;
            }
            // SPEC-20261023-1218: chave nova no formato do DICT; a MESMA chave antiga que não normaliza fica como está. Sem a
            // chave no corpo, fica a gravada (o tipo sozinho não muda nada)
            tournament.ResponsiblePixKey = request.Sent.Contains(nameof(request.ResponsiblePixKey))
                ? PixKeyNormalizer.NormalizeForUpdate(request.ResponsiblePixKey, request.ResponsiblePixKeyType, kept.ResponsiblePixKey)
                : kept.ResponsiblePixKey;
        }

        // If the blind structure changed (or was set when previously null), copy levels
        var addedDefaultCosts = false;
        if (request.BlindStructureId.HasValue && request.BlindStructureId != previousStructureId)
        {
            foreach (var existing in tournament.BlindLevels.ToList())
                _db.TournamentBlindLevels.Remove(existing);

            var structure = await _db.BlindStructures
                .Include(bs => bs.Levels)
                .Include(bs => bs.DefaultCosts)
                .AsSplitQuery()
                .FirstOrDefaultAsync(bs => bs.Id == request.BlindStructureId.Value, ct);
            if (structure != null)
            {
                // Isolamento: só template global (HomeGameId nulo) ou estrutura do próprio home game
                if (structure.HomeGameId.HasValue && structure.HomeGameId != tournament.HomeGameId)
                    throw new DomainException("Estrutura de blinds não pertence a este home game.");

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

                // Custos padrão (CP4): trocar A → B não cria, não apaga e não altera custos. Única exceção:
                // rascunho que estava SEM estrutura, recebe a primeira e ainda não tem nenhum custo Default
                // ("esqueci de escolher a estrutura na criação") — inclusive o de dealer, com DealerCount.
                if (previousStructureId is null
                    && await _plans.HasFeatureForHomeGameAsync(tournament.HomeGameId, PlanFeature.CostExtras, ct)
                    && !await _db.CostExtras.AnyAsync(
                        c => c.TournamentId == tournament.Id && c.CostType == nameof(CostType.Default), ct))
                {
                    foreach (var cost in BuildDefaultCostExtras(structure, request.DealerCount ?? 1, tournament.CreatedBy))
                    {
                        cost.TournamentId = tournament.Id;
                        _db.CostExtras.Add(cost);
                        addedDefaultCosts = true;
                    }
                }
            }
        }

        // Garante custos automáticos caso staff/ranking tenham sido definidos só na edição. O de ranking só nasce quando a
        // configuração do ranking muda: excluído na aba Custos, não volta numa edição qualquer (SPEC-20260717-1321)
        var createdAutoCosts = await EnsureAutoCostsAsync(
            tournament, hasPayments, rankingChanged: !before.SameRanking(MoneyValues.Of(tournament)), ct);

        // Knockout (SPEC-20261026-1909): null no pedido = manter; o bounty é conferido contra o buy-in/rebuy novos. Mudar
        // formato ou valor num rascunho que já tem inscrições refaz as cabeças e o total — só sem eliminação valendo
        // (a transferência já feita ficaria com o valor velho). Depois de começar, só o valor pode mudar (o tipo foi
        // conferido acima).
        var changedKnockout = await ApplyKnockoutOnUpdateAsync(tournament, request, keptKnockout, ct);

        // Divulgação (SPEC-20261108-1639): campo ausente = manter; ao ficar público pela primeira vez o slug nasce. Só para
        // quem gerencia o home game — membro comum edita o resto e os campos de divulgação do corpo são ignorados. Subir ou
        // tirar o limite de vagas promove quem couber na lista de espera (a trava do torneio já está tomada acima)
        var oldMax = tournament.MaxPlayers;
        if (await ManagesAsync(tournament.HomeGameId, updatedBy, updatedByIsAdmin, ct) && TournamentPublicity.Apply(tournament, request))
            await TournamentPublicity.EnsureSlugAsync(_db, tournament, await SlugClubNameAsync(tournament.HomeGameId, ct), ct);
        await PromoteWaitlistIfRoomAsync(tournament, oldMax, tournament.MaxPlayers, ct);

        var after = MoneyValues.Of(tournament);
        var moneyChanged = !before.SameMoney(after);
        // Os prêmios confirmados são % do líquido de agora: mudar valor deixaria a premiação desencontrada. Também mexem no
        // líquido sem mudar valor nenhum (revisão da 1927): o custo automático criado nesta edição (ex.: o acumulado do
        // ranking que tinha sido excluído volta ao trocar o ranking) e os custos padrão da primeira estrutura
        if ((moneyChanged || createdAutoCosts.Count > 0 || addedDefaultCosts) && tournament.PrizeConfirmed)
            throw new DomainException(PrizeConfirmedValuesMessage);

        if (moneyChanged || createdAutoCosts.Count > 0)
        {
            // Devido de cada inscrição, lançamentos, custos automáticos e totais (SPEC-20261103-1927)
            var recalculated = await RecalcMoneyAsync(tournament, before, createdAutoCosts, ct);
            if (moneyChanged)
                RecordValuesChange(tournament, before, after, recalculated, updatedBy);
        }
        else if (addedDefaultCosts || changedKnockout)
        {
            // Custos padrão entraram num rascunho (que pode já ter inscrições): totais e ranking % na mesma gravação
            await RecalcTotalsWithPendingAsync(tournament, ct);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // A página pública mostra nome, data, valores e vagas: a edição derruba a cópia em cache; as promoções da espera
        // (se houve) saem agora
        ForgetPublicPage(tournament);
        await _notifier.PushPendingAsync(ct);

        return await ToResponseAsync(tournament, ct);
    }

    /// <summary>
    /// Valores de dinheiro do torneio que mexem no devido dos jogadores ou nos totais (SPEC-20261103-1927). Nulo e zero
    /// são a mesma coisa (o formulário manda 0 no lugar do nulo); contribuição ao ranking sem modo ou com valor 0 = nenhuma.
    /// </summary>
    private sealed record MoneyValues(
        decimal BuyIn, decimal Rebuy, decimal Addon, decimal Staff, Guid? RankingId, string RankingMode, decimal RankingValue,
        string KnockoutType, decimal Bounty)
    {
        public const string NoRanking = "";

        public static MoneyValues Of(Tournament t)
        {
            var hasContribution = t.RankingContribMode is not null && t.RankingContribValue is > 0;
            return new MoneyValues(
                t.BuyInAmount, t.RebuyAmount ?? 0m, t.AddonAmount ?? 0m, t.StaffAmount ?? 0m, t.RankingId,
                hasContribution ? t.RankingContribMode! : NoRanking, hasContribution ? t.RankingContribValue!.Value : 0m,
                t.KnockoutType, t.BountyAmount ?? 0m);
        }

        /// <summary>Ranking por jogador cobrado de cada inscrição (o % sai do bolo, não do jogador)</summary>
        public decimal RankingFixed => RankingMode == "PerPlayer" ? RankingValue : 0m;

        public bool SameRanking(MoneyValues o) =>
            RankingId == o.RankingId && RankingMode == o.RankingMode && RankingValue == o.RankingValue;

        public bool SameMoney(MoneyValues o) =>
            BuyIn == o.BuyIn && Rebuy == o.Rebuy && Addon == o.Addon && Staff == o.Staff
            && RankingMode == o.RankingMode && RankingValue == o.RankingValue
            && KnockoutType == o.KnockoutType && Bounty == o.Bounty;
    }

    /// <summary>
    /// Recálculo do dinheiro depois de mudar valores do torneio (SPEC-20261103-1927). Cada inscrição (jogando ou não) recebe
    /// o buy-in novo, rebuys × rebuy e add-ons × add-on e passa pelo mesmo <see cref="EntryService.RecalculateBalance"/>
    /// (devido com staff e ranking fixo, saldo e status; o pago não muda). Os lançamentos de buy-in, rebuy e add-on ainda
    /// valendo passam a ter o valor novo — o desfazer tira do bolo o valor do lançamento. Bolo = soma dos devidos. Custos
    /// automáticos pela diferença (o ajuste feito à mão na aba Custos fica): staff + (novo − antigo) × inscrições; ranking por
    /// jogador idem (recém-criado ou vindo do %, começa de novo: valor × inscrições); ranking %, custos que encolheram, total
    /// de custos e líquido pela regra de sempre (<see cref="RecalcTotalsWithPendingAsync"/>, com
    /// <see cref="KnockoutRules.ApplyNet"/>). Devolve quantas inscrições foram recalculadas. Não salva.
    /// </summary>
    private async Task<int> RecalcMoneyAsync(
        Tournament tournament, MoneyValues before, IReadOnlySet<string> createdAutoCosts, CancellationToken ct)
    {
        var entries = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournament.Id)
            .ToListAsync(ct);

        // Valor zerado com quem já comprou apagaria a dívida sem lançamento para desfazer
        if (tournament.RebuyAmount is not > 0 && entries.Any(e => e.RebuyCount > 0))
            throw new DomainException(RebuyValueRequiredMessage);
        if (tournament.AddonAmount is not > 0 && entries.Any(e => e.AddonCount > 0))
            throw new DomainException(AddonValueRequiredMessage);

        var rebuy = tournament.RebuyAmount ?? 0m;
        var addon = tournament.AddonAmount ?? 0m;
        foreach (var e in entries)
        {
            e.BuyInAmount = tournament.BuyInAmount;
            e.RebuyTotal = e.RebuyCount * rebuy;
            e.AddonAmount = e.AddonCount > 0 ? e.AddonCount * addon : 0m;
            EntryService.RecalculateBalance(e, tournament);
        }

        var charges = await _db.Transactions
            .Where(t => t.TournamentId == tournament.Id && t.ReversedAt == null
                     && (t.Type == nameof(TransactionType.BuyIn)
                         || t.Type == nameof(TransactionType.Rebuy)
                         || t.Type == nameof(TransactionType.Addon)))
            .ToListAsync(ct);
        foreach (var t in charges)
        {
            t.Amount = t.Type switch
            {
                nameof(TransactionType.BuyIn) => tournament.BuyInAmount,
                nameof(TransactionType.Rebuy) => rebuy,
                _ => addon * AddonRules.Units(t), // o antigo "Addon Duplo" vale 2
            };
        }

        tournament.TotalPrizePool = entries.Sum(e => e.TotalDue);

        // Custos gravados + os recém-criados pelo EnsureAutoCostsAsync (ainda no Local)
        await _db.CostExtras.Where(c => c.TournamentId == tournament.Id).LoadAsync(ct);
        var costs = _db.CostExtras.Local.Where(c => c.TournamentId == tournament.Id).ToList();
        var after = MoneyValues.Of(tournament);
        var count = entries.Count;

        if (costs.FirstOrDefault(c => c.CostType == "Staff") is { } staffCost)
            staffCost.Amount = createdAutoCosts.Contains("Staff")
                ? after.Staff * count
                : Math.Max(0m, staffCost.Amount + (after.Staff - before.Staff) * count);

        // Ranking em % é refeito pela fórmula no RecalcTotalsWithPendingAsync
        if (costs.FirstOrDefault(c => c.CostType == "RankingAccumulated") is { } rankingCost && after.RankingMode != "Percent")
            rankingCost.Amount = before.RankingMode == "Percent" || createdAutoCosts.Contains("RankingAccumulated")
                ? after.RankingFixed * count
                : Math.Max(0m, rankingCost.Amount + (after.RankingFixed - before.RankingFixed) * count);

        await RecalcTotalsWithPendingAsync(tournament, ct);
        return count;
    }

    /// <summary>
    /// Histórico e auditoria da mudança de valores (SPEC-20261103-1927): a atividade visível "Valores do torneio alterados:
    /// buy-in R$ 100,00 → R$ 120,00 · …" explica por que o devido dos jogadores mudou; o AuditLog guarda o antes e o depois.
    /// </summary>
    private void RecordValuesChange(Tournament tournament, MoneyValues before, MoneyValues after, int entries, Guid? userId)
    {
        var changes = new List<string>();
        void Add(string label, decimal from, decimal to)
        {
            if (from != to) changes.Add($"{label} {Money(from)} → {Money(to)}");
        }
        Add("buy-in", before.BuyIn, after.BuyIn);
        Add("rebuy", before.Rebuy, after.Rebuy);
        Add("add-on", before.Addon, after.Addon);
        Add("staff", before.Staff, after.Staff);
        if (before.RankingMode != after.RankingMode || before.RankingValue != after.RankingValue)
            changes.Add($"ranking {RankingText(before)} → {RankingText(after)}");
        Add("bounty", before.Bounty, after.Bounty);

        if (entries > 0)
        {
            var text = $"Valores do torneio alterados: {string.Join(" · ", changes)}. Devido recalculado de "
                + (entries == 1 ? "1 jogador." : $"{entries} jogadores.");
            _db.TournamentActivities.Add(new TournamentActivity
            {
                TournamentId = tournament.Id,
                Type = nameof(TournamentActivityType.TournamentValuesChanged),
                Description = text.Length > 1000 ? text[..997] + "..." : text,
                CreatedBy = userId
            });
        }

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Tournament",
            EntityId = tournament.Id,
            Action = "ValuesChange",
            OldValues = System.Text.Json.JsonSerializer.Serialize(before),
            NewValues = System.Text.Json.JsonSerializer.Serialize(after),
            UserId = userId,
            TournamentId = tournament.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static string RankingText(MoneyValues v) => v.RankingMode switch
    {
        "PerPlayer" => $"{Money(v.RankingValue)} por jogador",
        "Percent" => $"{v.RankingValue.ToString("0.##", PtBrNumber)}% do líquido",
        _ => "sem contribuição"
    };

    /// <summary>
    /// Formato/bounty na edição do rascunho (SPEC-20261026-1909). Devolve true quando mudou (cabeças e total refeitos).
    /// </summary>
    private async Task<bool> ApplyKnockoutOnUpdateAsync(
        Tournament tournament, CreateTournamentRequest request, (string Type, decimal? Bounty) kept, CancellationToken ct)
    {
        var type = KnockoutRules.Parse(request.KnockoutType) ?? kept.Type;
        var bounty = request.BountyAmount ?? kept.Bounty;
        (tournament.KnockoutType, tournament.BountyAmount) =
            KnockoutRules.Resolve(type, bounty, tournament.BuyInAmount, tournament.RebuyAmount);

        if (tournament.KnockoutType == kept.Type && tournament.BountyAmount == kept.Bounty)
            return false;

        if (await _db.Eliminations.AnyAsync(e => e.TournamentId == tournament.Id && !e.Corrected, ct))
            throw new DomainException(KnockoutRules.ChangeWithEliminationsMessage);

        var entries = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournament.Id)
            .ToListAsync(ct);
        KnockoutRules.RecomputeFromScratch(tournament, entries);
        return true;
    }

    /// <summary>
    /// Cria (idempotente) os custos automáticos de staff/ranking se faltarem. Sem o recurso Payments
    /// (Mesa) não cria nada — e não mexe nos campos já gravados (queda de plano não apaga nada). O de ranking só quando
    /// a configuração do ranking mudou nesta edição (<paramref name="rankingChanged"/>, SPEC-20261103-1927): o custo
    /// excluído na aba Custos não renasce numa edição qualquer. Devolve os tipos criados (Amount 0, ainda não salvos).
    /// </summary>
    private async Task<HashSet<string>> EnsureAutoCostsAsync(
        Tournament tournament, bool hasPayments, bool rankingChanged, CancellationToken ct)
    {
        var created = new HashSet<string>();
        if (!hasPayments)
            return created;

        var existing = await _db.CostExtras
            .Where(c => c.TournamentId == tournament.Id)
            .ToListAsync(ct);

        if (tournament.StaffAmount is > 0 && existing.All(c => c.CostType != "Staff"))
        {
            created.Add("Staff");
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
                PixKeyType = PixKeyNormalizer.TypeOf(homeGame?.PixKey),
                PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending),
            });
        }

        if (rankingChanged && tournament.RankingId.HasValue && tournament.RankingContribMode is not null
            && tournament.RankingContribValue is > 0
            && existing.All(c => c.CostType != "RankingAccumulated"))
        {
            created.Add("RankingAccumulated");
            _db.CostExtras.Add(new CostExtra
            {
                TournamentId = tournament.Id,
                Description = "Acumulado do ranking",
                Amount = 0,
                CostType = "RankingAccumulated",
                PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending),
            });
        }

        return created;
    }

    /// <summary>Quantidade de dealers do pedido (CP17): null = não informado (vale 1); fora de 0–20 → 400.</summary>
    private static void EnsureValidDealerCount(int? dealerCount)
    {
        if (dealerCount is < 0 or > MaxDealerCount)
            throw new DomainException("Informe de 0 a 20 dealers.");
    }

    /// <summary>
    /// Custos que a estrutura lança no torneio (SPEC-20261023-1215): um CostExtra Default por custo padrão, na
    /// ordem da estrutura, com descrição, valor, beneficiário e chave PIX copiados; depois, se a estrutura tem
    /// dealer pago e <paramref name="dealerCount"/> &gt; 0, um único "Dealer" (1) ou "Dealers (N × R$ X)" = N × valor,
    /// sem beneficiário nem PIX (cada dealer recebe à parte). Todos Pending, CreatedBy = quem criou o torneio.
    /// </summary>
    private static List<CostExtra> BuildDefaultCostExtras(BlindStructure structure, int dealerCount, Guid? createdBy)
    {
        var costs = structure.DefaultCosts
            .OrderBy(c => c.SortOrder)
            .Select(c => new CostExtra
            {
                Description = c.Description,
                Amount = c.Amount,
                Beneficiary = c.Beneficiary,
                PixKey = c.PixKey,
                // Tipo derivado da chave (SPEC-20261023-1218): a chave já foi normalizada ao salvar a estrutura (ou
                // no boot); uma chave antiga fora de formato é copiada como está, sem tipo
                PixKeyType = PixKeyNormalizer.TypeOf(c.PixKey),
                CostType = nameof(CostType.Default),
                PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending),
                CreatedBy = createdBy,
            })
            .ToList();

        if (structure.DefaultHasDealer && structure.DefaultDealerFee is > 0 && dealerCount > 0)
        {
            var fee = structure.DefaultDealerFee.Value;
            costs.Add(new CostExtra
            {
                Description = dealerCount == 1 ? "Dealer" : $"Dealers ({dealerCount} × {Money(fee)})",
                Amount = dealerCount * fee,
                CostType = nameof(CostType.Default),
                PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending),
                CreatedBy = createdBy,
            });
        }

        return costs;
    }

    private static string Money(decimal value) => $"R$ {value.ToString("N2", PtBrNumber)}";

    /// <summary>
    /// Totais do torneio com os custos gravados + os recém-adicionados (ainda não salvos): ranking % = múltiplo
    /// de 10 de % × (receita − staff − custos comuns − bounties), TotalCosts = soma, NetPrizePool = receita − custos −
    /// bounties (SPEC-20261026-1909: bounty não é prize pool).
    /// GOTCHA: a mesma fórmula está em EntryService.RecalcAutoCostsAndTotals e em
    /// CostExtrasController.RecomputeTournamentFinancials — mudar nos três.
    /// </summary>
    private async Task RecalcTotalsWithPendingAsync(Tournament tournament, CancellationToken ct)
    {
        // Carrega (e rastreia) os gravados; o Local junta os adicionados nesta requisição e tira os removidos
        await _db.CostExtras.Where(c => c.TournamentId == tournament.Id).LoadAsync(ct);
        var costs = _db.CostExtras.Local.Where(c => c.TournamentId == tournament.Id).ToList();

        if (tournament.RankingContribMode == "Percent" && tournament.RankingContribValue is > 0)
        {
            var rankingCost = costs.FirstOrDefault(c => c.CostType == "RankingAccumulated");
            if (rankingCost is not null)
            {
                var staff = costs.Where(c => c.CostType == "Staff").Sum(c => c.Amount);
                var common = costs
                    .Where(c => c.CostType != "Staff" && c.CostType != "RankingAccumulated")
                    .Sum(c => c.Amount);
                var baseForPct = tournament.TotalPrizePool - staff - common - tournament.TotalBounties;
                var raw = (tournament.RankingContribValue!.Value / 100m) * baseForPct;
                rankingCost.Amount = Math.Max(0m, Math.Round(raw / 10m, MidpointRounding.AwayFromZero) * 10m);
            }
        }

        // Mesma regra do EntryService: custo que encolheu abaixo do que já recebeu de jogadores fica Pago, com o pago
        // limitado ao valor (o excedente aparece no detalhamento)
        await CostCredits.FitAllAsync(_db, tournament.Id, costs, ct);

        tournament.TotalCosts = costs.Sum(c => c.Amount);
        KnockoutRules.ApplyNet(tournament);
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

        // SPEC-20261029-1720 (D2): no fim da estrutura o relógio fica em 00:00 e o organizador acrescenta níveis para
        // continuar. O tempo que passou parado em 00:00 não pode ir para o nível novo (o auto-avanço do relógio levaria o
        // excedente e pularia níveis): o último nível fica "recém-terminado" e o seguinte começa do zero.
        var oldLast = tournament.BlindLevels.OrderByDescending(l => l.LevelNumber).FirstOrDefault();
        if (oldLast is not null && (tournament.CurrentLevel ?? 0) >= oldLast.LevelNumber)
        {
            var duration = oldLast.DurationMinutes * 60;
            var elapsed = tournament.TimerElapsedSeconds
                + (tournament.IsTimerRunning && tournament.TimerStartedAt.HasValue
                    ? (int)(DateTimeOffset.UtcNow - tournament.TimerStartedAt.Value).TotalSeconds
                    : 0);
            if ((tournament.CurrentLevel ?? 0) > oldLast.LevelNumber || elapsed > duration)
            {
                tournament.CurrentLevel = oldLast.LevelNumber;
                tournament.TimerElapsedSeconds = duration;
                tournament.TimerStartedAt = tournament.IsTimerRunning ? DateTimeOffset.UtcNow : null;
            }
        }

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

        await ApplyStatusTransitionAsync(tournament, newStatus, userId, ct);

        await _db.SaveChangesAsync(ct);
        // A página pública mostra o status (cancelado dá 404 na hora): derruba a cópia em cache
        ForgetPublicPage(tournament);
        // "Começou" / resultado para as contas vinculadas (SPEC-20261108-1635), já gravados junto com o status
        await _notifier.PushPendingAsync(ct);
        return await ToResponseAsync(tournament, ct);
    }

    /// <summary>
    /// A transição de status com as regras, os efeitos e a auditoria (StatusChange), sem salvar — a mesma para o botão do
    /// painel (<see cref="UpdateStatusAsync"/>) e para o relógio ligado num torneio que ainda não começou (TimerService,
    /// SPEC-20261103-1927: rascunho → Em andamento também pelo token do dealer). O torneio vem rastreado.
    /// </summary>
    public async Task ApplyStatusTransitionAsync(
        Tournament tournament, string newStatus, Guid? userId, CancellationToken ct = default)
    {
        if (!Enum.TryParse<TournamentStatus>(newStatus, out var status))
            throw new DomainException($"Status inválido: {newStatus}");

        ValidateStatusTransition(tournament.Status, newStatus);

        // Já foi finalizado uma vez (e reaberto pelo desfazer eliminação): não cancela — senão o torneio jogado até o
        // fim devolveria a vaga do limite semanal (revisão geral 2)
        if (status == TournamentStatus.Cancelled && tournament.FirstFinishedAt is not null)
            throw new DomainException(FinishedOnceCannotBeCancelledMessage);

        var oldStatus = tournament.Status;
        tournament.Status = newStatus;

        // Ações específicas por transição de status
        switch (status)
        {
            case TournamentStatus.InProgress:
                // Liga o relógio se está parado (do ponto em que parou). Já andando (rascunho com o relógio ligado antes da
                // SPEC-20261103-1927, ou a volta do intervalo de acerto), não mexe: zerar o início jogaria fora o tempo que
                // já passou e o relógio voltaria
                if (!tournament.IsTimerRunning)
                {
                    tournament.TimerStartedAt = DateTimeOffset.UtcNow;
                    tournament.IsTimerRunning = true;
                    tournament.TimerPausedAt = null;
                }
                // Começou de verdade (rascunho/inscrições abertas → em andamento): avisa as contas vinculadas
                // (SPEC-20261108-1635). A volta do intervalo de acerto não é um começo.
                if (IsBeforeStart(oldStatus))
                    await _notifier.StageStartedAsync(tournament, ct);
                break;

            case TournamentStatus.Finished:
                tournament.IsTimerRunning = false;
                tournament.FirstFinishedAt ??= DateTimeOffset.UtcNow;
                // Resultado (posição e prêmio) para as contas vinculadas (SPEC-20261108-1635)
                await _notifier.StageResultAsync(tournament, ct);
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
                // Finalizado não cancela mais (SPEC-20261026-1907); a reversão fica como defesa para um aporte que
                // tenha sobrado gravado (o "desfazer" que reabre o torneio já devolve o acumulado).
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
    }

    public const string FinishedCannotBeCancelledMessage = "Torneio finalizado não pode ser cancelado.";

    public const string FinishedOnceCannotBeCancelledMessage = "Este torneio já foi finalizado; não pode ser cancelado.";

    /// <summary>
    /// Transições permitidas. SPEC-20261026-1907: torneio finalizado NÃO pode ser cancelado (400 com
    /// <see cref="FinishedCannotBeCancelledMessage"/>) — cancelado não conta no limite semanal do plano, e cancelar um
    /// torneio já jogado liberaria a vaga. Antes de finalizar dá para cancelar em qualquer status, inclusive durante o
    /// acerto (BreakSettlement), que é o mesmo torneio ainda em jogo.
    /// </summary>
    private static void ValidateStatusTransition(string currentStatus, string newStatus)
    {
        var validTransitions = new Dictionary<string, string[]>
        {
            [nameof(TournamentStatus.Draft)] = [nameof(TournamentStatus.OpenForRegistration), nameof(TournamentStatus.InProgress), nameof(TournamentStatus.Cancelled)],
            [nameof(TournamentStatus.OpenForRegistration)] = [nameof(TournamentStatus.InProgress), nameof(TournamentStatus.Cancelled)],
            [nameof(TournamentStatus.InProgress)] = [nameof(TournamentStatus.BreakSettlement), nameof(TournamentStatus.Finished), nameof(TournamentStatus.Cancelled)],
            [nameof(TournamentStatus.BreakSettlement)] = [nameof(TournamentStatus.InProgress), nameof(TournamentStatus.Finished), nameof(TournamentStatus.Cancelled)],
            [nameof(TournamentStatus.Finished)] = [],
            [nameof(TournamentStatus.Cancelled)] = []
        };

        if (currentStatus == nameof(TournamentStatus.Finished) && newStatus == nameof(TournamentStatus.Cancelled))
            throw new DomainException(FinishedCannotBeCancelledMessage);

        if (!validTransitions.TryGetValue(currentStatus, out var allowed) || !allowed.Contains(newStatus))
            throw new DomainException($"Transição de status inválida: {currentStatus} → {newStatus}");
    }
}
