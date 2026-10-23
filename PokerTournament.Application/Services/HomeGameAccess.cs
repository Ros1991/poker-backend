using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Application.Services;

/// <summary>
/// Mesma regra do TenantAccessFilter (Api) para quem não tem o home game na rota:
/// acessa quem é dono (HomeGame.OwnerId), membro (HomeGameMembers) ou Admin do sistema.
/// </summary>
public static class HomeGameAccess
{
    public static async Task<bool> CanAccessAsync(
        IAppDbContext db, Guid homeGameId, Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        if (isAdmin)
            return true;
        if (await db.HomeGames.AnyAsync(h => h.Id == homeGameId && h.OwnerId == userId, ct))
            return true;
        return await db.HomeGameMembers.AnyAsync(m => m.HomeGameId == homeGameId && m.UserId == userId, ct);
    }

    /// <summary>
    /// Pode gerenciar (criar/editar/apagar recursos do home game): dono, membro administrador
    /// (HomeGameMember.IsAdmin) ou Admin do sistema.
    /// </summary>
    public static async Task<bool> CanManageAsync(
        IAppDbContext db, Guid homeGameId, Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        if (isAdmin)
            return true;
        if (await db.HomeGames.AnyAsync(h => h.Id == homeGameId && h.OwnerId == userId, ct))
            return true;
        return await db.HomeGameMembers.AnyAsync(
            m => m.HomeGameId == homeGameId && m.UserId == userId && m.IsAdmin, ct);
    }

    /// <summary>
    /// Administra pelo menos um home game: dono de algum, membro administrador de algum ou
    /// Admin do sistema. É quem pode buscar contas por e-mail (GET /Users/lookup) — o modal
    /// "Adicionar Membro" só existe para quem administra.
    /// </summary>
    public static async Task<bool> ManagesAnyAsync(
        IAppDbContext db, Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        if (isAdmin)
            return true;
        if (await db.HomeGames.AnyAsync(h => h.OwnerId == userId, ct))
            return true;
        return await db.HomeGameMembers.AnyAsync(m => m.UserId == userId && m.IsAdmin, ct);
    }

    /// <summary>Home games de que o usuário é dono ou membro.</summary>
    public static Task<List<Guid>> AccessibleHomeGameIdsAsync(
        IAppDbContext db, Guid userId, CancellationToken ct = default) =>
        db.HomeGames.Where(h => h.OwnerId == userId).Select(h => h.Id)
            .Union(db.HomeGameMembers.Where(m => m.UserId == userId).Select(m => m.HomeGameId))
            .ToListAsync(ct);
}
