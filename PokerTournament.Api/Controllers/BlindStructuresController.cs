using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class BlindStructuresController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;

    public BlindStructuresController(IAppDbContext db, IMapper mapper)
    {
        _db = db;
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
            query = query.Where(bs => bs.HomeGameId == homeGameId || bs.HomeGameId == null);

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
        // Verificar permissão se vinculada a um home game
        if (request.HomeGameId.HasValue)
        {
            var allowed = await CanManageHomeGame(request.HomeGameId.Value, ct);
            if (!allowed) return Forbid();
        }

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

        // Verificar permissão pelo home game da estrutura
        var homeGameId = structure.HomeGameId ?? request.HomeGameId;
        if (homeGameId.HasValue)
        {
            var allowed = await CanManageHomeGame(homeGameId.Value, ct);
            if (!allowed) return Forbid();
        }

        structure.Name = request.Name;
        structure.Description = request.Description;
        structure.HomeGameId = request.HomeGameId;
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

        // Qualquer autenticado pode deletar/editar qualquer estrutura

        // Hard delete
        _db.BlindStructures.Remove(structure);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Verifica se o usuário logado pode gerenciar o home game:
    /// owner do HG, admin member do HG, ou admin do sistema
    /// </summary>
    private async Task<bool> CanManageHomeGame(Guid homeGameId, CancellationToken ct)
    {
        var userId = GetUserId();
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

        if (userRole == "Admin") return true;

        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == homeGameId, ct);
        if (homeGame == null) return false;
        if (homeGame.OwnerId == userId) return true;

        return await _db.HomeGameMembers
            .AnyAsync(m => m.HomeGameId == homeGameId && m.UserId == userId && m.IsAdmin, ct);
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}
