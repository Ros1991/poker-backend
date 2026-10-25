using System.Globalization;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Pix;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

public class TournamentService
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;
    private readonly PlanService _plans;

    /// <summary>Quantidade de dealers aceita na criação (CP17 da SPEC-20261023-1215).</summary>
    public const int MaxDealerCount = 20;

    // Valores em pt-BR sem depender de ICU/cultura do servidor: R$ 1.234,56 (mesmo formato do TournamentHistoryService)
    private static readonly NumberFormatInfo PtBrNumber = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberDecimalDigits = 2
    };

    public TournamentService(IAppDbContext db, IMapper mapper, PlanService plans)
    {
        _db = db;
        _mapper = mapper;
        _plans = plans;
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
    }

    /// <summary>
    /// Cria o torneio. Limite do plano do dono (SPEC-20261023-1205): no Mesa, 1 torneio criado a cada
    /// 7 dias (402 tournaments_per_week com nextAllowedAt); o Admin do sistema passa
    /// <paramref name="bypassPlanLimits"/> = true. Sem o recurso Payments (Mesa) o torneio nasce sem
    /// custos automáticos de staff/ranking, sem ranking e sem PIX do dia (ver StripPaidFields).
    /// Tudo numa transação com trava por dono: dois cliques simultâneos não criam dois torneios no Mesa.
    /// SPEC-20261023-1215: com a estrutura escolhida, o torneio já nasce com os custos padrão dela (CostType
    /// Default, Pending) e, se ela tem dealer pago, com "Dealers (N × R$ X)" (N = DealerCount, null = 1) — só se o
    /// dono tem o recurso CostExtras (no Mesa nada é copiado); TotalCosts/NetPrizePool na mesma gravação.
    /// </summary>
    public async Task<TournamentResponse> CreateAsync(
        Guid homeGameId, CreateTournamentRequest request, Guid? createdBy = null,
        bool bypassPlanLimits = false, CancellationToken ct = default)
    {
        var homeGame = await _db.HomeGames
            .FirstOrDefaultAsync(h => h.Id == homeGameId, ct)
            ?? throw new DomainException("Home game não encontrado.");
        EnsureValidDealerCount(request.DealerCount);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Quem cria: o 402 de quem não é o dono sai sem o uso/horário dos home games do dono
        await _plans.EnsureCanCreateTournamentAsync(homeGameId, bypassPlanLimits, createdBy, ct);

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
        if (request.BlindStructureId.HasValue)
        {
            var structure = await _db.BlindStructures
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
        tournament.NetPrizePool = tournament.TotalPrizePool - tournament.TotalCosts;

        _db.Tournaments.Add(tournament);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

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

    public async Task<TournamentResponse> UpdateAsync(
        Guid id, CreateTournamentRequest request, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.BlindLevels)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (tournament.Status != nameof(TournamentStatus.Draft))
            throw new DomainException("Apenas torneios em rascunho podem ser editados.");
        EnsureValidDealerCount(request.DealerCount);

        var previousStructureId = tournament.BlindStructureId;
        // Queda de plano não apaga nada: sem Payments (Mesa) a edição mantém o que já estava gravado (staff,
        // ranking, contribuição e PIX do dia de um torneio criado no plano pago) e só ignora valores NOVOS da
        // requisição. Torneio criado no Mesa tem tudo nulo e continua nulo.
        var kept = (tournament.StaffAmount, tournament.RankingId, tournament.RankingContribMode,
            tournament.RankingContribValue, tournament.ResponsiblePixKey);
        _mapper.Map(request, tournament);
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
            // SPEC-20261023-1218: chave nova no formato do DICT; a MESMA chave antiga que não normaliza fica como está
            tournament.ResponsiblePixKey = PixKeyNormalizer.NormalizeForUpdate(
                request.ResponsiblePixKey, request.ResponsiblePixKeyType, kept.ResponsiblePixKey);
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

        // Garante custos automáticos caso staff/ranking tenham sido definidos só na edição.
        await EnsureAutoCostsAsync(tournament, hasPayments, ct);

        // Custos padrão entraram num rascunho (que pode já ter inscrições): totais e ranking % na mesma gravação
        if (addedDefaultCosts)
            await RecalcTotalsWithPendingAsync(tournament, ct);

        await _db.SaveChangesAsync(ct);

        return await ToResponseAsync(tournament, ct);
    }

    /// <summary>
    /// Cria (idempotente) os custos automáticos de staff/ranking se faltarem. Sem o recurso Payments
    /// (Mesa) não cria nada — e não mexe nos campos já gravados (queda de plano não apaga nada).
    /// </summary>
    private async Task EnsureAutoCostsAsync(Tournament tournament, bool hasPayments, CancellationToken ct)
    {
        if (!hasPayments)
            return;

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
                PixKeyType = PixKeyNormalizer.TypeOf(homeGame?.PixKey),
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
    /// de 10 de % × (receita − staff − custos comuns), TotalCosts = soma, NetPrizePool = receita − custos.
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
                var baseForPct = tournament.TotalPrizePool - staff - common;
                var raw = (tournament.RankingContribValue!.Value / 100m) * baseForPct;
                rankingCost.Amount = Math.Max(0m, Math.Round(raw / 10m, MidpointRounding.AwayFromZero) * 10m);
            }
        }

        tournament.TotalCosts = costs.Sum(c => c.Amount);
        tournament.NetPrizePool = tournament.TotalPrizePool - tournament.TotalCosts;
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
        return await ToResponseAsync(tournament, ct);
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
