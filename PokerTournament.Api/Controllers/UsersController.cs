using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    public const string LookupNotFoundMessage =
        "Nenhuma conta confirmada com este e-mail. A pessoa precisa criar a conta e confirmar o e-mail.";

    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;
    private readonly AuthService _auth;

    public UsersController(IAppDbContext db, IMapper mapper, AuthService auth)
    {
        _db = db;
        _mapper = mapper;
        _auth = auth;
    }

    /// <summary>Lista completa (nome e e-mail de todo mundo): só Admin do sistema.</summary>
    [HttpGet]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<List<UserResponse>>> GetAll(CancellationToken ct)
    {
        var users = await _db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        return Ok(_mapper.Map<List<UserResponse>>(users));
    }

    /// <summary>
    /// Busca de conta por e-mail EXATO (sem distinguir maiúsculas), só entre ativas, para o
    /// modal "Adicionar Membro". Não é oráculo de contas: só quem administra algum home game
    /// (dono, membro administrador ou Admin do sistema) usa, com no máximo 10 buscas por
    /// minuto por usuário (política "user-lookup"). Nunca lista, nunca aceita prefixo/contains
    /// e nunca devolve o papel. Registrada antes de {id:guid} de propósito ("lookup" não é guid).
    /// Conta com o e-mail ainda não confirmado não é encontrada (SPEC-20261023-1209, D16.6).
    /// </summary>
    [HttpGet("lookup")]
    [EnableRateLimiting("user-lookup")]
    public async Task<ActionResult<UserLookupResponse>> Lookup(
        [FromQuery] string? email, CancellationToken ct)
    {
        if (!await HomeGameAccess.ManagesAnyAsync(_db, GetUserId(), IsAdmin, ct))
            return new ObjectResult(new { message = "Só quem administra um home game pode buscar contas." }) { StatusCode = 403 };

        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "Informe o e-mail." });

        // Contas antigas que diferem só por maiúsculas: a igual ao digitado, senão a mais antiga.
        // Só contas com o e-mail confirmado (D16.6): conta recém-criada e não confirmada não aparece.
        var user = await UserEmail.MatchingEmail(_db.Users.Where(u => u.IsActive && u.EmailConfirmedAt != null), email)
            .Select(u => new UserLookupResponse
            {
                Id = u.Id,
                Name = u.FullName,
                Email = u.Email,
                PhotoUrl = u.PhotoUrl
            })
            .FirstOrDefaultAsync(ct);

        if (user is null)
            return NotFound(new { message = LookupNotFoundMessage });

        return Ok(user);
    }

    /// <summary>Ficha do usuário: Admin do sistema ou o próprio usuário.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> GetById(Guid id, CancellationToken ct)
    {
        if (!IsAdmin && id != GetUserId())
            return new ObjectResult(new { message = "Você não tem acesso a este usuário." }) { StatusCode = 403 };

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
            return NotFound(new { message = "Usuário não encontrado." });

        return Ok(_mapper.Map<UserResponse>(user));
    }

    [HttpPost]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<UserResponse>> Create(
        [FromBody] CreateUserRequest request, CancellationToken ct)
    {
        // Sem papel no corpo → Player; papel fora do enum → 400
        var role = string.IsNullOrWhiteSpace(request.Role) ? nameof(UserRole.Player) : request.Role;
        if (!CreateUserRequest.IsValidRole(role))
            return BadRequest(new { message = "Papel inválido." });

        var email = UserEmail.Normalize(request.Email);
        if (email.Length == 0)
            return BadRequest(new { message = "Informe o e-mail." });

        // Sem distinguir maiúsculas (o índice ix_users_email_lower garante o mesmo no banco)
        if (await UserEmail.IsTakenAsync(_db.Users, email, null, ct))
            return Conflict(new { message = "E-mail já está em uso." });

        var user = new User
        {
            FullName = request.FullName,
            Nickname = request.Nickname,
            Email = email, // sempre em minúsculas no banco
            Whatsapp = request.Whatsapp,
            PasswordHash = AuthService.HashPassword(request.Password),
            Role = role,
            IsActive = true,
            // D16.7: criada pelo Admin, que responde por ela — nasce confirmada
            EmailConfirmedAt = DateTimeOffset.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = user.Id },
            _mapper.Map<UserResponse>(user));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<UserResponse>> Update(
        Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Role) && !CreateUserRequest.IsValidRole(request.Role))
            return BadRequest(new { message = "Papel inválido." });

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
            return NotFound(new { message = "Usuário não encontrado." });

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = UserEmail.Normalize(request.Email);
            if (email != user.Email)
            {
                // Sem distinguir maiúsculas e ignorando a própria conta
                if (await UserEmail.IsTakenAsync(_db.Users, email, id, ct))
                    return Conflict(new { message = "E-mail já está em uso." });

                user.Email = email; // sempre em minúsculas no banco
            }
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
            user.FullName = request.FullName;

        if (request.Nickname != null)
            user.Nickname = request.Nickname;

        // Papel novo ou senha nova: os aparelhos logados caem (o token antigo
        // carrega o papel antigo e a senha antiga pode ter vazado)
        var revokeSessions = false;
        if (!string.IsNullOrWhiteSpace(request.Role) && request.Role != user.Role)
        {
            user.Role = request.Role;
            revokeSessions = true;
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = AuthService.HashPassword(request.Password);
            revokeSessions = true;
        }

        await _db.SaveChangesAsync(ct);
        if (revokeSessions)
            await _auth.RevokeAllSessionsAsync(user.Id, ct);
        return Ok(_mapper.Map<UserResponse>(user));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
            return NotFound(new { message = "Usuário não encontrado." });

        user.IsActive = false;
        await _db.SaveChangesAsync(ct);
        // Desativado: nenhum aparelho continua logado
        await _auth.RevokeAllSessionsAsync(user.Id, ct);

        return NoContent();
    }

    private bool IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "Admin";

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out var id) ? id : Guid.Empty;
    }
}

public class CreateUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Whatsapp { get; set; }
    public string Role { get; set; } = nameof(UserRole.Player);

    /// <summary>Papel válido = um dos nomes de <see cref="UserRole"/> (Admin, TournamentDirector, Dealer, Player).</summary>
    public static bool IsValidRole(string? role) =>
        role is not null && Enum.GetNames<UserRole>().Contains(role);
}

public class UpdateUserRequest
{
    public string? FullName { get; set; }
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
}
