using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Application.Services;

/// <summary>
/// Início da conta (SPEC-20261029-1724, D7 do plano de 2026-10-29): o que está acontecendo agora e os torneios de cada
/// home game que a conta acessa — os dela e os de outras pessoas em que ela é membro (o mesmo conjunto do
/// GET HomeGames). O Admin do sistema não ganha nada a mais aqui (vê os dele e aqueles em que é membro); só o
/// "gerencia" leva o papel em conta, como em <see cref="HomeGameAccess.CanManageAsync"/>.
/// </summary>
public class InicioService
{
    /// <summary>Quantos torneios recentes por home game.</summary>
    public const int RecentCount = 5;

    private static readonly string[] LiveStatuses =
    [
        nameof(TournamentStatus.OpenForRegistration),
        nameof(TournamentStatus.InProgress),
        nameof(TournamentStatus.BreakSettlement),
    ];

    private static readonly string[] NotFinishedStatuses =
    [
        nameof(TournamentStatus.Draft),
        .. LiveStatuses,
    ];

    private readonly IAppDbContext _db;
    private readonly PlanService _plans;

    public InicioService(IAppDbContext db, PlanService plans)
    {
        _db = db;
        _plans = plans;
    }

    /// <summary>Null = conta inexistente.</summary>
    public async Task<InicioResponse?> GetAsync(Guid userId, bool isSystemAdmin, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.FullName })
            .FirstOrDefaultAsync(ct);
        if (user is null)
            return null;

        var plan = await _plans.GetUserEffectivePlanAsync(userId, ct);
        var definition = PlanCatalog.Get(plan);

        // Os mesmos home games do GET HomeGames (ativos, dono ou membro), com o papel de quem está logado
        var homeGames = await _db.HomeGames.AsNoTracking()
            .Where(h => h.IsActive && (h.OwnerId == userId || h.Members.Any(m => m.UserId == userId)))
            .Select(h => new
            {
                h.Id,
                h.Name,
                h.IsPrimary,
                h.IsActive,
                h.OwnerId,
                OwnerName = h.Owner.FullName,
                MemberAdmin = h.Members.Any(m => m.UserId == userId && m.IsAdmin),
            })
            .ToListAsync(ct);

        var ordered = homeGames
            .OrderByDescending(h => h.OwnerId == userId)
            .ThenByDescending(h => h.OwnerId == userId && h.IsPrimary)
            .ThenBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var ids = ordered.Select(h => h.Id).ToList();

        var ownerPlans = new Dictionary<Guid, string>();
        foreach (var ownerId in ordered.Select(h => h.OwnerId).Distinct())
            ownerPlans[ownerId] = await _plans.GetUserEffectivePlanAsync(ownerId, ct);

        var cancelled = nameof(TournamentStatus.Cancelled);
        var finished = nameof(TournamentStatus.Finished);
        var counts = ids.Count == 0
            ? new Dictionary<Guid, int>()
            : await _db.Tournaments.AsNoTracking()
                .Where(t => ids.Contains(t.HomeGameId) && t.Status != cancelled)
                .GroupBy(t => t.HomeGameId)
                .Select(g => new { HomeGameId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.HomeGameId, x => x.Count, ct);

        var response = new InicioResponse
        {
            Plan = plan,
            HomeGamesVisible = definition.HomeGamesVisible,
            MaxOwnedHomeGames = definition.MaxOwnedHomeGames,
            PrimaryHomeGameId = ordered.FirstOrDefault(h => h.OwnerId == userId && h.IsPrimary)?.Id,
        };

        foreach (var h in ordered)
        {
            var ownerPlan = ownerPlans[h.OwnerId];
            var item = new InicioHomeGame
            {
                Id = h.Id,
                Name = h.Name,
                DisplayName = HomeGamePrimary.DisplayNameFor(h.IsPrimary, ownerPlan, h.Name),
                IsPrimary = h.IsPrimary,
                IsNameDefault = h.IsPrimary && HomeGamePrimary.IsDefaultName(h.Name, h.OwnerName),
                IsActive = h.IsActive,
                IsOwner = h.OwnerId == userId,
                CanManage = isSystemAdmin || h.OwnerId == userId || h.MemberAdmin,
                OwnerId = h.OwnerId,
                OwnerName = h.OwnerName,
                OwnerPlan = ownerPlan,
                TournamentCount = counts.GetValueOrDefault(h.Id),
            };

            var tournaments = _db.Tournaments.AsNoTracking().Where(t => t.HomeGameId == h.Id);
            item.Recent = await tournaments
                .Where(t => t.Status != cancelled)
                .OrderByDescending(t => t.Date)
                .Take(RecentCount)
                .Select(t => new InicioTournament
                {
                    Id = t.Id, Name = t.Name, Status = t.Status, Date = t.Date,
                    TotalEntries = t.TotalEntries, PlayersRemaining = t.PlayersRemaining,
                })
                .ToListAsync(ct);
            item.Next = await tournaments
                .Where(t => NotFinishedStatuses.Contains(t.Status))
                .OrderBy(t => t.Date)
                .Select(t => new InicioTournament
                {
                    Id = t.Id, Name = t.Name, Status = t.Status, Date = t.Date,
                    TotalEntries = t.TotalEntries, PlayersRemaining = t.PlayersRemaining,
                })
                .FirstOrDefaultAsync(ct);
            item.Last = await tournaments
                .Where(t => t.Status == finished)
                .OrderByDescending(t => t.Date)
                .Select(t => new InicioTournament
                {
                    Id = t.Id, Name = t.Name, Status = t.Status, Date = t.Date,
                    TotalEntries = t.TotalEntries, PlayersRemaining = t.PlayersRemaining,
                })
                .FirstOrDefaultAsync(ct);
            response.HomeGames.Add(item);
        }

        if (ids.Count > 0)
        {
            var live = await _db.Tournaments.AsNoTracking()
                .Where(t => ids.Contains(t.HomeGameId) && LiveStatuses.Contains(t.Status))
                .OrderByDescending(t => t.Date)
                .Select(t => new
                {
                    t.Id, t.Name, t.Status, t.Date, t.TotalEntries, t.PlayersRemaining, t.HomeGameId,
                    t.CurrentLevel, t.IsTimerRunning,
                })
                .ToListAsync(ct);
            var byId = response.HomeGames.ToDictionary(h => h.Id);
            response.Live = live
                .Select(t => new InicioLiveTournament
                {
                    Id = t.Id, Name = t.Name, Status = t.Status, Date = t.Date,
                    TotalEntries = t.TotalEntries, PlayersRemaining = t.PlayersRemaining,
                    HomeGameId = t.HomeGameId,
                    HomeGameName = byId[t.HomeGameId].DisplayName,
                    CurrentLevel = t.CurrentLevel,
                    IsTimerRunning = t.IsTimerRunning,
                    CanManage = byId[t.HomeGameId].CanManage,
                })
                .ToList();
        }

        return response;
    }
}
