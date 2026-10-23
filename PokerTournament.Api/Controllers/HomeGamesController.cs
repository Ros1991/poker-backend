using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// A rota é só {id} (sem {homeGameId}), então o TenantAccessFilter não age aqui: as leituras
/// por id conferem dono/membro/Admin via <see cref="HomeGameScopedControllerBase"/>.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class HomeGamesController : HomeGameScopedControllerBase
{
    private readonly IMapper _mapper;

    public HomeGamesController(IAppDbContext db, IMapper mapper) : base(db)
    {
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<List<HomeGameResponse>>> GetAll(CancellationToken ct)
    {
        var userId = GetUserId();

        var homeGames = await _db.HomeGames
            .Include(h => h.Members)
            .Include(h => h.Tournaments)
            .Where(h => h.IsActive && (
                h.OwnerId == userId ||
                h.Members.Any(m => m.UserId == userId )
            ))
            .OrderBy(h => h.Name)
            .ToListAsync(ct);

        return Ok(_mapper.Map<List<HomeGameResponse>>(homeGames));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<HomeGameResponse>> GetById(Guid id, CancellationToken ct)
    {
        var homeGame = await _db.HomeGames
            .Include(h => h.Members)
            .Include(h => h.Tournaments)
            .FirstOrDefaultAsync(h => h.Id == id, ct);

        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        // 403 (e não 404) como o filtro: o id não é segredo, o conteúdo (PIX, defaults) é
        if (!await CanAccessAsync(id, ct))
            return Forbidden();

        return Ok(_mapper.Map<HomeGameResponse>(homeGame));
    }

    [HttpPost]
    public async Task<ActionResult<HomeGameResponse>> Create(
        [FromBody] CreateHomeGameRequest request, CancellationToken ct)
    {
        var userId = GetUserId();

        var homeGame = _mapper.Map<HomeGame>(request);
        homeGame.OwnerId = userId;

        _db.HomeGames.Add(homeGame);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = homeGame.Id },
            _mapper.Map<HomeGameResponse>(homeGame));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<HomeGameResponse>> Update(
        Guid id, [FromBody] CreateHomeGameRequest request, CancellationToken ct)
    {
        var userId = GetUserId();

        var homeGame = await _db.HomeGames
            .FirstOrDefaultAsync(h => h.Id == id, ct);

        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        if (homeGame.OwnerId != userId)
            return Forbid();

        _mapper.Map(request, homeGame);
        await _db.SaveChangesAsync(ct);

        return Ok(_mapper.Map<HomeGameResponse>(homeGame));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();

        var homeGame = await _db.HomeGames
            .FirstOrDefaultAsync(h => h.Id == id, ct);

        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        if (homeGame.OwnerId != userId)
            return Forbid();

        homeGame.IsActive = false;
        homeGame.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<List<HomeGameMemberResponse>>> GetMembers(Guid id, CancellationToken ct)
    {
        if (!await _db.HomeGames.AnyAsync(h => h.Id == id, ct))
            return NotFound(new { message = "Home game não encontrado." });

        if (!await CanAccessAsync(id, ct))
            return Forbidden();

        var members = await _db.HomeGameMembers
            .Include(m => m.User)
            .Include(m => m.Person)
            .Where(m => m.HomeGameId == id )
            .OrderBy(m => m.User.FullName)
            .ToListAsync(ct);

        return Ok(_mapper.Map<List<HomeGameMemberResponse>>(members));
    }

    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<HomeGameMemberResponse>> AddMember(
        Guid id, [FromBody] AddHomeGameMemberRequest request, CancellationToken ct)
    {
        var userId = GetUserId();

        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        if (homeGame.OwnerId != userId)
        {
            var isAdmin = await _db.HomeGameMembers
                .AnyAsync(m => m.HomeGameId == id && m.UserId == userId && m.IsAdmin , ct);
            if (!isAdmin)
                return Forbid();
        }

        var exists = await _db.HomeGameMembers
            .AnyAsync(m => m.HomeGameId == id && m.UserId == request.UserId, ct);

        if (exists)
            return Conflict(new { message = "Usuário já é membro deste home game." });

        // Vínculo só com jogador/dealer DESTE home game (pessoa apagada não conta: filtro de soft delete)
        if (request.PersonId is { } personId
            && !await _db.Persons.AnyAsync(p => p.Id == personId && p.HomeGameId == id, ct))
            return BadRequest(new { message = "Jogador não pertence a este home game." });

        var member = new HomeGameMember
        {
            HomeGameId = id,
            UserId = request.UserId,
            PersonId = request.PersonId,
            IsAdmin = request.IsAdmin,
            JoinedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        _db.HomeGameMembers.Add(member);
        await _db.SaveChangesAsync(ct);

        member = await _db.HomeGameMembers
            .Include(m => m.User)
            .Include(m => m.Person)
            .FirstAsync(m => m.Id == member.Id, ct);

        return Ok(_mapper.Map<HomeGameMemberResponse>(member));
    }

    [HttpPut("{id:guid}/members/{memberId:guid}")]
    public async Task<ActionResult<HomeGameMemberResponse>> UpdateMember(
        Guid id, Guid memberId, [FromBody] UpdateHomeGameMemberRequest request, CancellationToken ct)
    {
        var userId = GetUserId();

        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        if (homeGame.OwnerId != userId)
        {
            var isAdmin = await _db.HomeGameMembers
                .AnyAsync(m => m.HomeGameId == id && m.UserId == userId && m.IsAdmin , ct);
            if (!isAdmin)
                return Forbid();
        }

        var member = await _db.HomeGameMembers
            .Include(m => m.User)
            .Include(m => m.Person)
            .FirstOrDefaultAsync(m => m.Id == memberId && m.HomeGameId == id, ct);

        if (member is null)
            return NotFound(new { message = "Membro não encontrado." });

        if (request.IsAdmin.HasValue)
            member.IsAdmin = request.IsAdmin.Value;

        if (request.PersonId is { } personId)
        {
            // Vínculo só com jogador/dealer DESTE home game (pessoa apagada não conta: filtro de soft delete)
            if (!await _db.Persons.AnyAsync(p => p.Id == personId && p.HomeGameId == id, ct))
                return BadRequest(new { message = "Jogador não pertence a este home game." });

            member.PersonId = personId;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(_mapper.Map<HomeGameMemberResponse>(member));
    }

    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    public async Task<ActionResult> RemoveMember(Guid id, Guid memberId, CancellationToken ct)
    {
        var userId = GetUserId();

        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        if (homeGame.OwnerId != userId)
        {
            var isAdmin = await _db.HomeGameMembers
                .AnyAsync(m => m.HomeGameId == id && m.UserId == userId && m.IsAdmin , ct);
            if (!isAdmin)
                return Forbid();
        }

        var member = await _db.HomeGameMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.HomeGameId == id, ct);

        if (member is null)
            return NotFound(new { message = "Membro não encontrado." });

        // Hard delete - remove o membro completamente
        _db.HomeGameMembers.Remove(member);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}

public class AddHomeGameMemberRequest
{
    public Guid UserId { get; set; }
    public Guid? PersonId { get; set; }
    public bool IsAdmin { get; set; }
}

public class UpdateHomeGameMemberRequest
{
    public bool? IsAdmin { get; set; }
    public Guid? PersonId { get; set; }
}
