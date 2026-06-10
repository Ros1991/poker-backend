using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Api.Filters;

/// <summary>
/// SEC-01: isolamento multi-tenant. Garante que o usuário só acessa recursos de
/// home games dos quais é dono, membro, ou se for Admin do sistema.
/// Resolve o HomeGameId a partir do {homeGameId} ou {tournamentId} da rota.
///
/// Controlável por configuração: Security:EnforceTenantIsolation (default true).
/// Se travar acesso legítimo, defina Security__EnforceTenantIsolation=false no ambiente.
/// </summary>
public class TenantAccessFilter : IAsyncActionFilter
{
    private readonly IConfiguration _config;

    public TenantAccessFilter(IConfiguration config)
    {
        _config = config;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var enforce = _config.GetValue<bool?>("Security:EnforceTenantIsolation") ?? true;
        if (!enforce)
        {
            await next();
            return;
        }

        var user = context.HttpContext.User;

        // Endpoints anônimos / não autenticados: deixa o pipeline de auth decidir.
        if (user.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        // Admin de sistema passa direto.
        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        if (role == "Admin")
        {
            await next();
            return;
        }

        var routeValues = context.RouteData.Values;

        Guid? homeGameId = null;
        if (routeValues.TryGetValue("homeGameId", out var hgv)
            && Guid.TryParse(hgv?.ToString(), out var hg))
            homeGameId = hg;

        Guid? tournamentId = null;
        if (routeValues.TryGetValue("tournamentId", out var tv)
            && Guid.TryParse(tv?.ToString(), out var tid))
            tournamentId = tid;

        // Rota não escopada a tenant (sem homeGameId/tournamentId): não é responsabilidade deste filtro.
        if (homeGameId is null && tournamentId is null)
        {
            await next();
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<IAppDbContext>();

        var userIdStr = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            context.Result = Forbidden();
            return;
        }

        // Resolver o HomeGameId a partir do torneio, se necessário.
        if (homeGameId is null && tournamentId is not null)
        {
            homeGameId = await db.Tournaments
                .Where(t => t.Id == tournamentId.Value)
                .Select(t => (Guid?)t.HomeGameId)
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            // Torneio inexistente: deixa o controller responder 404.
            if (homeGameId is null)
            {
                await next();
                return;
            }
        }

        var homeGame = await db.HomeGames
            .FirstOrDefaultAsync(h => h.Id == homeGameId!.Value, context.HttpContext.RequestAborted);
        if (homeGame is null)
        {
            await next();
            return;
        }

        var isOwner = homeGame.OwnerId == userId;
        var isMember = await db.HomeGameMembers
            .AnyAsync(m => m.HomeGameId == homeGameId.Value && m.UserId == userId,
                context.HttpContext.RequestAborted);

        if (isOwner || isMember)
        {
            await next();
            return;
        }

        context.Result = Forbidden();
    }

    private static ObjectResult Forbidden() =>
        new(new { message = "Você não tem acesso a este home game." }) { StatusCode = 403 };
}
