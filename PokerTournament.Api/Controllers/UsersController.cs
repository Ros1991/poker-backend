using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Só a busca de conta por e-mail exato do modal "Adicionar membro". A listagem, a ficha, editar, plano, senha e
/// status ficam em <c>api/v1/admin/users</c> (AdminUsersController, SPEC-20261023-1206); <c>GET/POST/PUT/DELETE
/// api/v1/Users</c> não existem mais e a administração não cria contas (D14).
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    public const string LookupNotFoundMessage =
        "Nenhuma conta confirmada com este e-mail. A pessoa precisa criar a conta e confirmar o e-mail.";

    private readonly IAppDbContext _db;

    public UsersController(IAppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Busca de conta por e-mail EXATO (sem distinguir maiúsculas), só entre ativas, para o
    /// modal "Adicionar Membro". Não é oráculo de contas: só quem administra algum home game
    /// (dono, membro administrador ou Admin do sistema) usa, com no máximo 10 buscas por
    /// minuto por usuário (política "user-lookup"). Nunca lista, nunca aceita prefixo/contains
    /// e nunca devolve o papel.
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

    private bool IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "Admin";

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out var id) ? id : Guid.Empty;
    }
}
