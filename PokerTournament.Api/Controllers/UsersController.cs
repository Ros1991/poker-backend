using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;

    public UsersController(IAppDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserResponse>>> GetAll(CancellationToken ct)
    {
        var users = await _db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        return Ok(_mapper.Map<List<UserResponse>>(users));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> GetById(Guid id, CancellationToken ct)
    {
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
        var existingEmail = await _db.Users
            .AnyAsync(u => u.Email == request.Email, ct);

        if (existingEmail)
            return Conflict(new { message = "E-mail já está em uso." });

        var user = new User
        {
            FullName = request.FullName,
            Nickname = request.Nickname,
            Email = request.Email,
            Whatsapp = request.Whatsapp,
            PasswordHash = AuthService.HashPassword(request.Password),
            Role = request.Role,
            IsActive = true
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
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
            return NotFound(new { message = "Usuário não encontrado." });

        if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != user.Email)
        {
            var emailInUse = await _db.Users
                .AnyAsync(u => u.Email == request.Email && u.Id != id, ct);

            if (emailInUse)
                return Conflict(new { message = "E-mail já está em uso." });

            user.Email = request.Email;
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
            user.FullName = request.FullName;

        if (request.Nickname != null)
            user.Nickname = request.Nickname;

        if (!string.IsNullOrWhiteSpace(request.Role))
            user.Role = request.Role;

        if (!string.IsNullOrWhiteSpace(request.Password))
            user.PasswordHash = AuthService.HashPassword(request.Password);

        await _db.SaveChangesAsync(ct);
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

        return NoContent();
    }
}

public class CreateUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Whatsapp { get; set; }
    public string Role { get; set; } = "Jogador";
}

public class UpdateUserRequest
{
    public string? FullName { get; set; }
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
}
