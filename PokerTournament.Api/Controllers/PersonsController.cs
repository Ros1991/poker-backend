using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// A rota não tem {homeGameId}/{tournamentId}, então o TenantAccessFilter não age aqui:
/// cada ação resolve o home game da pessoa (ou o da query) e aplica a mesma regra
/// (dono, membro ou Admin) via <see cref="HomeGameScopedControllerBase"/>.
/// Foto (SPEC-20261023-1213): POST/DELETE {id}/photo pelo <see cref="PhotoService"/> (JPEG/PNG/WebP até 2 MB,
/// conferidos pela assinatura; nome de arquivo novo a cada envio). Token de dealer nunca chega aqui (403 do
/// TokenKindHandler): o dealer põe foto por POST tournaments/{id}/players/{personId}/photo.
/// WhatsApp normalizado no servidor (F18) e repetido no home game → 400 com mensagem (antes estourava o índice).
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class PersonsController : HomeGameScopedControllerBase
{
    private readonly IMapper _mapper;
    private readonly PhotoService _photos;

    public PersonsController(IAppDbContext db, IMapper mapper, PhotoService photos) : base(db)
    {
        _mapper = mapper;
        _photos = photos;
    }

    /// <summary>
    /// Normaliza o que veio do formulário (nome sem espaços sobrando, vazio → null em apelido/e-mail, WhatsApp só
    /// com os dígitos locais) e confere nome obrigatório, WhatsApp e e-mail únicos no home game. Null = tudo certo.
    /// </summary>
    private async Task<string?> NormalizeAndValidateAsync(Person person, CancellationToken ct)
    {
        person.FullName = PlayerNames.NormalizeSpaces(person.FullName);
        if (person.FullName.Length == 0)
            return "Informe o nome completo do jogador.";
        if (person.FullName.Length > 200)
            return "O nome pode ter no máximo 200 caracteres.";

        var nickname = PlayerNames.NormalizeSpaces(person.Nickname);
        person.Nickname = nickname.Length == 0 ? null : nickname;
        if (nickname.Length > 100)
            return "O apelido pode ter no máximo 100 caracteres.";

        person.Email = string.IsNullOrWhiteSpace(person.Email) ? null : person.Email.Trim();
        person.Whatsapp = PhoneNumbers.NormalizeLocal(person.Whatsapp);
        if (person.Whatsapp is { Length: > 20 })
            return PhoneNumbers.InvalidWhatsappMessage;

        if (person.Whatsapp is { } whatsapp
            && await _db.Persons.AnyAsync(p => p.HomeGameId == person.HomeGameId && p.Id != person.Id && p.Whatsapp == whatsapp, ct))
            return TournamentPlayerService.DuplicateWhatsappMessage;

        if (person.Email is { } email
            && await _db.Persons.AnyAsync(p => p.HomeGameId == person.HomeGameId && p.Id != person.Id && p.Email == email, ct))
            return "Esse e-mail já está cadastrado em outro jogador do home game.";

        return null;
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
        // Foto só pelo upload (POST {id}/photo): nada de URL arbitrária vinda do formulário
        person.PhotoUrl = null;
        if (await NormalizeAndValidateAsync(person, ct) is { } error)
            return BadRequest(new { message = error });
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

        // Campos nulos no pedido não mudam nada (MappingProfile); "" limpa (apelido, e-mail, WhatsApp, notas)
        _mapper.Map(request, person);
        if (await NormalizeAndValidateAsync(person, ct) is { } error)
            return BadRequest(new { message = error });
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
    [RequestSizeLimit(ImageFiles.MaxRequestBytes)]
    public async Task<ActionResult<PersonResponse>> UploadPhoto(
        Guid id, IFormFile? file, CancellationToken ct)
    {
        var person = await _db.Persons
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (person is null)
            return NotFound(new { message = "Pessoa não encontrada." });
        if (!await CanAccessAsync(person.HomeGameId, ct))
            return Forbidden();
        if (file is null || file.Length == 0)
            return BadRequest(new { message = ImageFiles.InvalidPhotoMessage });

        await using var stream = file.OpenReadStream();
        await _photos.SetPersonPhotoAsync(person, stream, file.Length, ct: ct);

        return Ok(_mapper.Map<PersonResponse>(person));
    }

    /// <summary>Remove a foto (só o organizador: dono, membro ou Admin). PhotoUrl volta a null.</summary>
    [HttpDelete("{id:guid}/photo")]
    public async Task<ActionResult<PersonResponse>> RemovePhoto(Guid id, CancellationToken ct)
    {
        var person = await _db.Persons
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (person is null)
            return NotFound(new { message = "Pessoa não encontrada." });
        if (!await CanAccessAsync(person.HomeGameId, ct))
            return Forbidden();

        await _photos.RemovePersonPhotoAsync(person, ct);

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
}
