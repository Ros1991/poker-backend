using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Base para controllers cuja rota NÃO carrega {homeGameId}/{tournamentId} (o TenantAccessFilter
/// não age neles): cada ação resolve o home game do recurso e aplica aqui a mesma regra do filtro
/// — acessa quem é dono, membro ou Admin do sistema; gerencia quem é dono, membro administrador
/// ou Admin do sistema. Token de dealer (QR) nunca chega aqui: o TokenKindHandler já nega.
/// </summary>
public abstract class HomeGameScopedControllerBase : ControllerBase
{
    protected readonly IAppDbContext _db;

    protected HomeGameScopedControllerBase(IAppDbContext db)
    {
        _db = db;
    }

    /// <summary>Id do usuário logado (claim NameIdentifier ou sub); Guid.Empty se não houver.</summary>
    protected Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out var id) ? id : Guid.Empty;
    }

    /// <summary>Admin do sistema (papel do token), que ignora o isolamento por home game.</summary>
    protected bool IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "Admin";

    /// <summary>Dono, membro ou Admin do sistema.</summary>
    protected Task<bool> CanAccessAsync(Guid homeGameId, CancellationToken ct) =>
        HomeGameAccess.CanAccessAsync(_db, homeGameId, GetUserId(), IsAdmin, ct);

    /// <summary>Dono, membro administrador ou Admin do sistema.</summary>
    protected Task<bool> CanManageAsync(Guid homeGameId, CancellationToken ct) =>
        HomeGameAccess.CanManageAsync(_db, homeGameId, GetUserId(), IsAdmin, ct);

    /// <summary>403 com a mesma mensagem do TenantAccessFilter.</summary>
    protected static ObjectResult Forbidden() =>
        new(new { message = "Você não tem acesso a este home game." }) { StatusCode = 403 };
}
