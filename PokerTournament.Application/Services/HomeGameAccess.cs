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

    /// <summary>Home games de que o usuário é dono ou membro.</summary>
    public static Task<List<Guid>> AccessibleHomeGameIdsAsync(
        IAppDbContext db, Guid userId, CancellationToken ct = default) =>
        db.HomeGames.Where(h => h.OwnerId == userId).Select(h => h.Id)
            .Union(db.HomeGameMembers.Where(m => m.UserId == userId).Select(m => m.HomeGameId))
            .ToListAsync(ct);
}
