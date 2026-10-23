using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// A rota é só {id} (sem {homeGameId}), então o TenantAccessFilter não age aqui.
/// Estrutura com home game: lê quem acessa o home game (dono/membro/Admin), escreve quem o
/// gerencia (dono/membro admin/Admin). Estrutura global (HomeGameId nulo) é template do sistema:
/// lê qualquer autenticado, escreve só Admin do sistema.
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
            bs.DefaultAddonDoubleAllowed,
            bs.DefaultLateRegistrationLevel,
            bs.DefaultRebuyUntilLevel,
            bs.DefaultSeatsPerTable,
            bs.DefaultStaffAmount,
            bs.DefaultRankingContribMode,
            bs.DefaultRankingContribValue,
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
            structure.DefaultAddonDoubleAllowed,
            structure.DefaultLateRegistrationLevel,
            structure.DefaultRebuyUntilLevel,
            structure.DefaultSeatsPerTable,
            structure.DefaultStaffAmount,
            structure.DefaultRankingContribMode,
            structure.DefaultRankingContribValue,
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
            DefaultAddonDoubleAllowed = request.DefaultAddonDoubleAllowed ?? false,
            DefaultLateRegistrationLevel = request.DefaultLateRegistrationLevel,
            DefaultRebuyUntilLevel = request.DefaultRebuyUntilLevel,
            DefaultSeatsPerTable = request.DefaultSeatsPerTable,
            DefaultStaffAmount = request.DefaultStaffAmount,
            DefaultRankingContribMode = request.DefaultRankingContribMode,
            DefaultRankingContribValue = request.DefaultRankingContribValue,
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
        structure.DefaultAddonDoubleAllowed = request.DefaultAddonDoubleAllowed ?? false;
        structure.DefaultLateRegistrationLevel = request.DefaultLateRegistrationLevel;
        structure.DefaultRebuyUntilLevel = request.DefaultRebuyUntilLevel;
        structure.DefaultSeatsPerTable = request.DefaultSeatsPerTable;
        structure.DefaultStaffAmount = request.DefaultStaffAmount;
        structure.DefaultRankingContribMode = request.DefaultRankingContribMode;
        structure.DefaultRankingContribValue = request.DefaultRankingContribValue;

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

        // Hard delete
        _db.BlindStructures.Remove(structure);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Escrita em estrutura: com home game, quem o gerencia (dono, membro admin ou Admin);
    /// global (sem home game), só Admin do sistema.
    /// </summary>
    private Task<bool> CanWriteAsync(Guid? homeGameId, CancellationToken ct) =>
        homeGameId.HasValue ? CanManageAsync(homeGameId.Value, ct) : Task.FromResult(IsAdmin);
}
