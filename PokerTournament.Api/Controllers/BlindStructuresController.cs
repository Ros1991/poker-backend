using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Addons;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Knockout;
using PokerTournament.Application.Pix;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// A rota é só {id} (sem {homeGameId}), então o TenantAccessFilter não age aqui.
/// Estrutura com home game: lê quem acessa o home game (dono/membro/Admin), escreve quem o
/// gerencia (dono/membro admin/Admin). Estrutura global (HomeGameId nulo) é template do sistema:
/// lê qualquer autenticado, escreve só Admin do sistema.
/// SPEC-20261023-1215: a estrutura guarda também os custos padrão de valor fixo por torneio (defaultCosts) e o
/// custo de dealer (defaultHasDealer/defaultDealerFee), em QUALQUER plano (sem 402: salvar estrutura é livre no
/// Mesa e queda de plano nunca apaga nada). Quem decide se o torneio recebe os custos é o TournamentService.
/// SPEC-20261026-1909: também o knockout padrão (defaultKnockoutType None|Simple|Progressive + defaultBountyAmount), em
/// qualquer plano; o torneio criado com a estrutura herda e pode trocar.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class BlindStructuresController : HomeGameScopedControllerBase
{
    private readonly IMapper _mapper;

    public BlindStructuresController(IAppDbContext db, IMapper mapper) : base(db)
    {
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        [FromQuery] Guid? homeGameId, CancellationToken ct)
    {
        var query = _db.BlindStructures
            .Include(bs => bs.Levels.OrderBy(l => l.LevelNumber))
            .Include(bs => bs.DefaultCosts)
            .AsSplitQuery()
            .Where(bs => bs.IsActive);

        if (homeGameId.HasValue)
        {
            if (!await CanAccessAsync(homeGameId.Value, ct))
                return Forbidden();
            query = query.Where(bs => bs.HomeGameId == homeGameId || bs.HomeGameId == null);
        }
        else if (!IsAdmin)
        {
            // Sem home game na query: só as globais e as dos home games do usuário
            var accessibleIds = (await HomeGameAccess.AccessibleHomeGameIdsAsync(_db, GetUserId(), ct))
                .Select(id => (Guid?)id)
                .ToList();
            query = query.Where(bs => bs.HomeGameId == null || accessibleIds.Contains(bs.HomeGameId));
        }

        var structures = await query
            .OrderBy(bs => bs.Name)
            .ToListAsync(ct);

        return Ok(structures.Select(bs => new
        {
            bs.Id,
            bs.Name,
            bs.Description,
            bs.HomeGameId,
            bs.DefaultBuyIn,
            bs.DefaultRebuy,
            bs.DefaultAddon,
            bs.DefaultStartingStack,
            bs.DefaultRebuyStack,
            bs.DefaultAddonStack,
            bs.DefaultMaxRebuys,
            bs.DefaultAddonAllowed,
            bs.DefaultMaxAddons,
            bs.DefaultLateRegistrationLevel,
            bs.DefaultRebuyUntilLevel,
            bs.DefaultSeatsPerTable,
            bs.DefaultStaffAmount,
            bs.DefaultRankingContribMode,
            bs.DefaultRankingContribValue,
            bs.DefaultHasDealer,
            bs.DefaultDealerFee,
            bs.DefaultKnockoutType,
            bs.DefaultBountyAmount,
            DefaultCosts = ProjectDefaultCosts(bs),
            LevelCount = bs.Levels.Count,
            Levels = bs.Levels.Select(l => new
            {
                l.Id,
                l.LevelNumber,
                l.SmallBlind,
                l.BigBlind,
                l.Ante,
                l.BigBlindAnte,
                l.DurationMinutes,
                l.IsBreak,
                l.BreakDescription
            })
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> GetById(Guid id, CancellationToken ct)
    {
        var structure = await _db.BlindStructures
            .Include(bs => bs.Levels.OrderBy(l => l.LevelNumber))
            .Include(bs => bs.DefaultCosts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(bs => bs.Id == id, ct);

        if (structure is null)
            return NotFound(new { message = "Estrutura não encontrada." });

        // Global (template do sistema): qualquer autenticado lê
        if (structure.HomeGameId.HasValue && !await CanAccessAsync(structure.HomeGameId.Value, ct))
            return Forbidden();

        return Ok(new
        {
            structure.Id,
            structure.Name,
            structure.Description,
            structure.HomeGameId,
            structure.DefaultBuyIn,
            structure.DefaultRebuy,
            structure.DefaultAddon,
            structure.DefaultStartingStack,
            structure.DefaultRebuyStack,
            structure.DefaultAddonStack,
            structure.DefaultMaxRebuys,
            structure.DefaultAddonAllowed,
            structure.DefaultMaxAddons,
            structure.DefaultLateRegistrationLevel,
            structure.DefaultRebuyUntilLevel,
            structure.DefaultSeatsPerTable,
            structure.DefaultStaffAmount,
            structure.DefaultRankingContribMode,
            structure.DefaultRankingContribValue,
            structure.DefaultHasDealer,
            structure.DefaultDealerFee,
            structure.DefaultKnockoutType,
            structure.DefaultBountyAmount,
            DefaultCosts = ProjectDefaultCosts(structure),
            Levels = structure.Levels.Select(l => new
            {
                l.Id,
                l.LevelNumber,
                l.SmallBlind,
                l.BigBlind,
                l.Ante,
                l.BigBlindAnte,
                l.DurationMinutes,
                l.IsBreak,
                l.BreakDescription
            })
        });
    }

    [HttpPost]
    public async Task<ActionResult> Create(
        [FromBody] CreateBlindStructureRequest request, CancellationToken ct)
    {
        if (!await CanWriteAsync(request.HomeGameId, ct))
            return Forbidden();

        // Custos padrão e dealer (SPEC-20261023-1215): valida antes de gravar (DomainException → 400)
        var defaultCosts = request.DefaultCosts is null ? [] : BuildDefaultCosts(request.DefaultCosts);
        var (hasDealer, dealerFee) = ResolveDealer(request.DefaultHasDealer ?? false, request.DefaultDealerFee);
        // Knockout padrão (SPEC-20261026-1909): null = Normal
        var (koType, koBounty) = KnockoutRules.ResolveStructureDefault(
            KnockoutRules.Parse(request.DefaultKnockoutType) ?? nameof(Domain.Enums.KnockoutType.None), request.DefaultBountyAmount,
            request.DefaultBuyIn);

        var structure = new BlindStructure
        {
            Name = request.Name,
            Description = request.Description,
            HomeGameId = request.HomeGameId,
            IsActive = true,
            DefaultBuyIn = request.DefaultBuyIn,
            DefaultRebuy = request.DefaultRebuy,
            DefaultAddon = request.DefaultAddon,
            DefaultStartingStack = request.DefaultStartingStack,
            DefaultRebuyStack = request.DefaultRebuyStack,
            DefaultAddonStack = request.DefaultAddonStack,
            DefaultMaxRebuys = request.DefaultMaxRebuys,
            DefaultAddonAllowed = request.DefaultAddonAllowed ?? true,
            DefaultMaxAddons = AddonRules.Resolve(request.DefaultMaxAddons, AddonRules.DefaultMax),
            DefaultLateRegistrationLevel = request.DefaultLateRegistrationLevel,
            DefaultRebuyUntilLevel = request.DefaultRebuyUntilLevel,
            DefaultSeatsPerTable = request.DefaultSeatsPerTable,
            DefaultStaffAmount = request.DefaultStaffAmount,
            DefaultRankingContribMode = request.DefaultRankingContribMode,
            DefaultRankingContribValue = request.DefaultRankingContribValue,
            DefaultHasDealer = hasDealer,
            DefaultDealerFee = dealerFee,
            DefaultKnockoutType = koType,
            DefaultBountyAmount = koBounty,
        };

        foreach (var levelRequest in request.Levels)
        {
            structure.Levels.Add(new BlindLevel
            {
                LevelNumber = levelRequest.LevelNumber,
                SmallBlind = levelRequest.SmallBlind,
                BigBlind = levelRequest.BigBlind,
                Ante = levelRequest.Ante,
                BigBlindAnte = levelRequest.BigBlindAnte,
                DurationMinutes = levelRequest.DurationMinutes,
                IsBreak = levelRequest.IsBreak,
                BreakDescription = levelRequest.BreakDescription,
            });
        }

        foreach (var cost in defaultCosts)
            structure.DefaultCosts.Add(cost);

        _db.BlindStructures.Add(structure);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = structure.Id },
            new { structure.Id, structure.Name });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(
        Guid id, [FromBody] CreateBlindStructureRequest request, CancellationToken ct)
    {
        var structure = await _db.BlindStructures
            .Include(bs => bs.Levels)
            .Include(bs => bs.DefaultCosts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(bs => bs.Id == id, ct);

        if (structure is null)
            return NotFound(new { message = "Estrutura não encontrada." });

        // HomeGameId nulo/ausente no request = manter o vínculo atual (template global continua
        // global; estrutura de home game continua no seu). Só muda com valor explícito.
        var targetHomeGameId = request.HomeGameId ?? structure.HomeGameId;

        // Precisa gerenciar a estrutura ATUAL e, se estiver mudando de home game, o destino também
        if (!await CanWriteAsync(structure.HomeGameId, ct))
            return Forbidden();
        if (targetHomeGameId != structure.HomeGameId && !await CanWriteAsync(targetHomeGameId, ct))
            return Forbidden();

        // Custos padrão (CP7): null/ausente = manter; lista (inclusive vazia) = substituir tudo.
        // Dealer: DefaultHasDealer null = manter os dois campos. Valida tudo antes de mexer na estrutura.
        // Chaves PIX já gravadas: a MESMA chave antiga que não normaliza pode voltar como está (SPEC-20261023-1218)
        var currentPixKeys = structure.DefaultCosts.Select(c => c.PixKey).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var newDefaultCosts = request.DefaultCosts is null ? null : BuildDefaultCosts(request.DefaultCosts, currentPixKeys);
        var (keepHasDealer, keepDealerFee) = request.DefaultHasDealer is null
            ? (structure.DefaultHasDealer, structure.DefaultDealerFee)
            : ResolveDealer(request.DefaultHasDealer.Value, request.DefaultDealerFee);
        // Knockout padrão (SPEC-20261026-1909): formato null = manter formato e valor (cliente que não conhece o campo)
        var requestedKo = KnockoutRules.Parse(request.DefaultKnockoutType);
        var (keepKoType, keepKoBounty) = requestedKo is null
            ? (structure.DefaultKnockoutType, structure.DefaultBountyAmount)
            : KnockoutRules.ResolveStructureDefault(requestedKo, request.DefaultBountyAmount, request.DefaultBuyIn);
        // Formato mantido com um buy-in padrão novo: o bounty guardado também precisa ficar abaixo dele (revisão geral 2)
        KnockoutRules.EnsureBelowDefaultBuyIn(keepKoType, keepKoBounty, request.DefaultBuyIn);
        // Máximo de add-ons padrão (SPEC-20261027-1311): null = manter; 0 = ilimitado
        var keepMaxAddons = AddonRules.Resolve(request.DefaultMaxAddons, structure.DefaultMaxAddons);

        structure.Name = request.Name;
        structure.Description = request.Description;
        structure.HomeGameId = targetHomeGameId;
        structure.DefaultBuyIn = request.DefaultBuyIn;
        structure.DefaultRebuy = request.DefaultRebuy;
        structure.DefaultAddon = request.DefaultAddon;
        structure.DefaultStartingStack = request.DefaultStartingStack;
        structure.DefaultRebuyStack = request.DefaultRebuyStack;
        structure.DefaultAddonStack = request.DefaultAddonStack;
        structure.DefaultMaxRebuys = request.DefaultMaxRebuys;
        structure.DefaultAddonAllowed = request.DefaultAddonAllowed ?? true;
        structure.DefaultMaxAddons = keepMaxAddons;
        structure.DefaultLateRegistrationLevel = request.DefaultLateRegistrationLevel;
        structure.DefaultRebuyUntilLevel = request.DefaultRebuyUntilLevel;
        structure.DefaultSeatsPerTable = request.DefaultSeatsPerTable;
        structure.DefaultStaffAmount = request.DefaultStaffAmount;
        structure.DefaultRankingContribMode = request.DefaultRankingContribMode;
        structure.DefaultRankingContribValue = request.DefaultRankingContribValue;
        structure.DefaultHasDealer = keepHasDealer;
        structure.DefaultDealerFee = keepDealerFee;
        structure.DefaultKnockoutType = keepKoType;
        structure.DefaultBountyAmount = keepKoBounty;

        // Remover levels existentes e recriar
        foreach (var level in structure.Levels.ToList())
            _db.BlindLevels.Remove(level);

        foreach (var levelRequest in request.Levels)
        {
            structure.Levels.Add(new BlindLevel
            {
                LevelNumber = levelRequest.LevelNumber,
                SmallBlind = levelRequest.SmallBlind,
                BigBlind = levelRequest.BigBlind,
                Ante = levelRequest.Ante,
                BigBlindAnte = levelRequest.BigBlindAnte,
                DurationMinutes = levelRequest.DurationMinutes,
                IsBreak = levelRequest.IsBreak,
                BreakDescription = levelRequest.BreakDescription,
                BlindStructureId = structure.Id,
            });
        }

        // Mesmo padrão dos níveis: remove todos e recria na ordem da lista. Torneios já criados não mudam
        // (os custos foram copiados para eles na criação — CP5).
        if (newDefaultCosts is not null)
        {
            foreach (var cost in structure.DefaultCosts.ToList())
                _db.BlindStructureDefaultCosts.Remove(cost);
            foreach (var cost in newDefaultCosts)
            {
                cost.BlindStructureId = structure.Id;
                structure.DefaultCosts.Add(cost);
            }
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { structure.Id, structure.Name });
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var structure = await _db.BlindStructures
            .FirstOrDefaultAsync(bs => bs.Id == id, ct);

        if (structure is null)
            return NotFound(new { message = "Estrutura não encontrada." });

        // Mesma regra do Update: gerencia o home game da estrutura; global só Admin
        if (!await CanWriteAsync(structure.HomeGameId, ct))
            return Forbidden();

        // Hard delete (níveis e custos padrão vão junto em cascata; os torneios já criados guardam as cópias)
        _db.BlindStructures.Remove(structure);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Teto de custos padrão por estrutura (CP1).</summary>
    public const int MaxDefaultCosts = 20;
    /// <summary>Teto do valor de cada custo padrão e do valor por dealer (cabe em decimal(10,2)).</summary>
    public const decimal MaxDefaultCostAmount = 99_999.99m;

    /// <summary>
    /// Valida e converte a lista do pedido (CP1, passo 3 da SPEC-20261023-1215): descrição obrigatória até 200,
    /// valor &gt; 0 e &lt;= 99.999,99, "para quem" até 200, no máximo 20 itens. Textos com Trim(); vazios viram null.
    /// SortOrder = posição na lista. Erro → DomainException (400 com a mensagem).
    /// Chave PIX (SPEC-20261023-1218): gravada no formato do DICT (PixKeyNormalizer; pixKeyType do item é a escolha
    /// da tela) e o tipo gravado é o da chave normalizada. Na edição, uma chave de <paramref name="currentPixKeys"/>
    /// (já gravada na estrutura) que não normaliza volta como está.
    /// </summary>
    private static List<BlindStructureDefaultCost> BuildDefaultCosts(
        List<CreateBlindStructureRequest.DefaultCostItem> items, IReadOnlySet<string>? currentPixKeys = null)
    {
        if (items.Count > MaxDefaultCosts)
            throw new DomainException("Uma estrutura pode ter no máximo 20 custos padrão.");

        var result = new List<BlindStructureDefaultCost>(items.Count);
        foreach (var item in items)
        {
            var description = item?.Description?.Trim();
            if (string.IsNullOrEmpty(description))
                throw new DomainException("Informe a descrição de cada custo padrão.");
            if (description.Length > 200)
                throw new DomainException("A descrição do custo padrão pode ter no máximo 200 caracteres.");
            if (item!.Amount <= 0)
                throw new DomainException("O valor de cada custo padrão precisa ser maior que zero.");
            if (item.Amount > MaxDefaultCostAmount)
                throw new DomainException("O valor de cada custo padrão pode ser no máximo R$ 99.999,99.");

            var beneficiary = NullIfEmpty(item.Beneficiary);
            if (beneficiary is { Length: > 200 })
                throw new DomainException("O campo \"Para quem\" do custo padrão pode ter no máximo 200 caracteres.");
            var typedPixKey = NullIfEmpty(item.PixKey);
            var pixKey = PixKeyNormalizer.NormalizeForUpdate(item.PixKey, item.PixKeyType,
                typedPixKey is not null && currentPixKeys?.Contains(typedPixKey) == true ? typedPixKey : null);
            if (pixKey is { Length: > 200 })
                throw new DomainException("A chave PIX do custo padrão pode ter no máximo 200 caracteres.");
            var pixKeyType = PixKeyNormalizer.TypeOf(pixKey);

            result.Add(new BlindStructureDefaultCost
            {
                Description = description,
                Amount = Math.Round(item.Amount, 2, MidpointRounding.AwayFromZero),
                Beneficiary = beneficiary,
                PixKey = pixKey,
                PixKeyType = pixKeyType,
                SortOrder = result.Count,
            });
        }
        return result;
    }

    /// <summary>
    /// Custo de dealer (CP16): com dealer pago, o valor por dealer é obrigatório (&gt; 0 e &lt;= 99.999,99);
    /// sem dealer, o valor é apagado.
    /// </summary>
    private static (bool HasDealer, decimal? Fee) ResolveDealer(bool hasDealer, decimal? fee)
    {
        if (!hasDealer)
            return (false, null);
        if (fee is not > 0 || fee > MaxDefaultCostAmount)
            throw new DomainException("Informe o valor por dealer.");
        return (true, Math.Round(fee.Value, 2, MidpointRounding.AwayFromZero));
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Custos padrão na resposta, na ordem da estrutura.</summary>
    private static IEnumerable<object> ProjectDefaultCosts(BlindStructure structure) =>
        structure.DefaultCosts
            .OrderBy(c => c.SortOrder)
            .Select(c => new
            {
                c.Id,
                c.Description,
                c.Amount,
                c.Beneficiary,
                c.PixKey,
                c.PixKeyType,
                c.SortOrder,
            })
            .ToList();

    /// <summary>
    /// Escrita em estrutura: com home game, quem o gerencia (dono, membro admin ou Admin);
    /// global (sem home game), só Admin do sistema.
    /// </summary>
    private Task<bool> CanWriteAsync(Guid? homeGameId, CancellationToken ct) =>
        homeGameId.HasValue ? CanManageAsync(homeGameId.Value, ct) : Task.FromResult(IsAdmin);
}
