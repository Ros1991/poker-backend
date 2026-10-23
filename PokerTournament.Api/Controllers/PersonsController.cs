using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// A rota não tem {homeGameId}/{tournamentId}, então o TenantAccessFilter não age aqui:
/// cada ação resolve o home game da pessoa (ou o da query) e aplica a mesma regra
/// (dono, membro ou Admin) via <see cref="HomeGameAccess"/>.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class PersonsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;

    public PersonsController(IAppDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult> GetHistory(Guid id, CancellationToken ct)
    {
        var person = await _db.Persons.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (person is null)
            return NotFound(new { message = "Pessoa não encontrada." });
        if (!await CanAccessAsync(person.HomeGameId, ct))
            return Forbidden();

        var history = await _db.TournamentEntries
            .Where(e => e.PersonId == id && e.FinalPosition != null)
            .OrderByDescending(e => e.Tournament.Date)
            .Select(e => new
            {
                tournamentId = e.TournamentId,
                tournamentName = e.Tournament.Name,
                date = e.Tournament.Date,
                position = e.FinalPosition,
                prize = e.PrizeAmount ?? 0m,
                points = e.RankingPoints ?? 0m,
            })
            .ToListAsync(ct);
        return Ok(history);
    }

    [HttpGet]
    public async Task<ActionResult<List<PersonResponse>>> GetAll(
        [FromQuery] Guid? homeGameId,
        [FromQuery] string? type,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        // Sem home game a lista seria a de todos os grupos do sistema
        if (homeGameId is null)
            return BadRequest(new { message = "Informe o home game (homeGameId)." });
        if (!await CanAccessAsync(homeGameId.Value, ct))
            return Forbidden();

        var query = _db.Persons.Where(p => p.IsActive && p.HomeGameId == homeGameId.Value);

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(p => p.Type == type);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLower();
            query = query.Where(p =>
                p.FullName.ToLower().Contains(term) ||
                (p.Nickname != null && p.Nickname.ToLower().Contains(term)) ||
                (p.Email != null && p.Email.ToLower().Contains(term)));
        }

        var items = await query
            .OrderBy(p => p.FullName)
            .ToListAsync(ct);

        return Ok(_mapper.Map<List<PersonResponse>>(items));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PersonResponse>> GetById(Guid id, CancellationToken ct)
    {
        var person = await _db.Persons
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (person is null)
            return NotFound(new { message = "Pessoa não encontrada." });
        if (!await CanAccessAsync(person.HomeGameId, ct))
            return Forbidden();

        return Ok(_mapper.Map<PersonResponse>(person));
    }

    [HttpPost]
    public async Task<ActionResult<PersonResponse>> Create(
        [FromBody] CreatePersonRequest request, CancellationToken ct)
    {
        if (!await CanAccessAsync(request.HomeGameId, ct))
            return Forbidden();

        var person = _mapper.Map<Person>(request);
        _db.Persons.Add(person);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = person.Id },
            _mapper.Map<PersonResponse>(person));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PersonResponse>> Update(
        Guid id, [FromBody] UpdatePersonRequest request, CancellationToken ct)
    {
        var person = await _db.Persons
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (person is null)
            return NotFound(new { message = "Pessoa não encontrada." });
        if (!await CanAccessAsync(person.HomeGameId, ct))
            return Forbidden();

        _mapper.Map(request, person);
        await _db.SaveChangesAsync(ct);

        return Ok(_mapper.Map<PersonResponse>(person));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var person = await _db.Persons
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (person is null)
            return NotFound(new { message = "Pessoa não encontrada." });
        if (!await CanAccessAsync(person.HomeGameId, ct))
            return Forbidden();

        person.IsActive = false;
        person.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/photo")]
    public async Task<ActionResult<PersonResponse>> UploadPhoto(
        Guid id, IFormFile file, CancellationToken ct)
    {
        var person = await _db.Persons
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (person is null)
            return NotFound(new { message = "Pessoa não encontrada." });
        if (!await CanAccessAsync(person.HomeGameId, ct))
            return Forbidden();

        // Salvar arquivo no disco
        var uploadsRoot = Environment.GetEnvironmentVariable("UPLOADS_PATH")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        var uploadsPath = Path.Combine(uploadsRoot, "photos");
        Directory.CreateDirectory(uploadsPath);

        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{person.Id}{extension}";
        var filePath = Path.Combine(uploadsPath, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream, ct);

        person.PhotoUrl = $"/uploads/photos/{fileName}";
        await _db.SaveChangesAsync(ct);

        return Ok(_mapper.Map<PersonResponse>(person));
    }

    /// <summary>Busca por nome/apelido. Sem homeGameId, só nos home games do usuário.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<PersonResponse>>> Search(
        [FromQuery] string q, [FromQuery] Guid? homeGameId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(new List<PersonResponse>());

        var query = _db.Persons.Where(p => p.IsActive);
        if (homeGameId is not null)
        {
            if (!await CanAccessAsync(homeGameId.Value, ct))
                return Forbidden();
            query = query.Where(p => p.HomeGameId == homeGameId.Value);
        }
        else if (!IsAdmin)
        {
            var homeGameIds = await HomeGameAccess.AccessibleHomeGameIdsAsync(_db, GetUserId(), ct);
            query = query.Where(p => homeGameIds.Contains(p.HomeGameId));
        }

        var term = q.ToLower();
        var persons = await query
            .Where(p =>
                p.FullName.ToLower().Contains(term) ||
                (p.Nickname != null && p.Nickname.ToLower().Contains(term)))
            .OrderBy(p => p.FullName)
            .Take(20)
            .ToListAsync(ct);

        return Ok(_mapper.Map<List<PersonResponse>>(persons));
    }

    private bool IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "Admin";

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out var id) ? id : Guid.Empty;
    }

    private Task<bool> CanAccessAsync(Guid homeGameId, CancellationToken ct) =>
        HomeGameAccess.CanAccessAsync(_db, homeGameId, GetUserId(), IsAdmin, ct);

    private static ObjectResult Forbidden() =>
        new(new { message = "Você não tem acesso a este home game." }) { StatusCode = 403 };
}
