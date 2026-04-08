using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Api.Controllers;

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

    [HttpGet]
    public async Task<ActionResult<List<PersonResponse>>> GetAll(
        [FromQuery] Guid? homeGameId,
        [FromQuery] string? type,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var query = _db.Persons.Where(p => p.IsActive);

        if (homeGameId.HasValue)
            query = query.Where(p => p.HomeGameId == homeGameId.Value);

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

        return Ok(_mapper.Map<PersonResponse>(person));
    }

    [HttpPost]
    public async Task<ActionResult<PersonResponse>> Create(
        [FromBody] CreatePersonRequest request, CancellationToken ct)
    {
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

    [HttpGet("search")]
    public async Task<ActionResult<List<PersonResponse>>> Search(
        [FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(new List<PersonResponse>());

        var term = q.ToLower();
        var persons = await _db.Persons
            .Where(p => p.IsActive &&
                (p.FullName.ToLower().Contains(term) ||
                 (p.Nickname != null && p.Nickname.ToLower().Contains(term))))
            .OrderBy(p => p.FullName)
            .Take(20)
            .ToListAsync(ct);

        return Ok(_mapper.Map<List<PersonResponse>>(persons));
    }
}
