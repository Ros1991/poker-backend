using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PokerTournament.Application.Common;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Application.Services;

/// <summary>
/// Lembrete no dia do torneio (SPEC-20261108-1639): a partir das 8 h (Brasília) do dia, para cada torneio de hoje não
/// cancelado/finalizado, avisa UMA vez (<c>tournament_reminders</c>) as reservas aprovadas ainda sem inscrição e os
/// inscritos vinculados à conta (<c>Person.UserId</c>). Roda no tick do <c>NotificationDigestJob</c> (a cada 10 min) e
/// no endpoint de desenvolvimento com relógio falso. A categoria "Torneio" desligada não cria o aviso, mas a linha do
/// lembrete é gravada mesmo assim (não insiste a cada 10 min).
/// </summary>
public class TournamentReminderService
{
    public const int SendFromHourBrt = 8;
    private static readonly TimeSpan Brt = TimeSpan.FromHours(-3);

    private readonly IAppDbContext _db;
    private readonly NotificationService _notifications;
    private readonly PlanService _plans;
    private readonly ILogger<TournamentReminderService> _logger;

    public TournamentReminderService(IAppDbContext db, NotificationService notifications, PlanService plans, ILogger<TournamentReminderService> logger)
    {
        _db = db;
        _notifications = notifications;
        _plans = plans;
        _logger = logger;
    }

    /// <summary>Devolve quantas contas receberam o lembrete nesta rodada.</summary>
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var brt = now.ToOffset(Brt);
        if (brt.Hour < SendFromHourBrt)
            return 0;
        var start = new DateTimeOffset(brt.Date, Brt);
        var end = start.AddDays(1);
        var finished = nameof(TournamentStatus.Finished);
        var cancelled = nameof(TournamentStatus.Cancelled);

        var tournaments = await _db.Tournaments.AsNoTracking()
            .Where(t => t.Date >= start && t.Date < end && t.Status != finished && t.Status != cancelled && t.HomeGame.DeletedAt == null)
            .Select(t => new
            {
                t.Id, t.Name, t.Date, t.HomeGameId,
                ClubName = t.HomeGame.Name, ClubIsPrimary = t.HomeGame.IsPrimary, OwnerId = t.HomeGame.OwnerId, OwnerName = t.HomeGame.Owner.FullName,
                t.HomeGame.District, t.HomeGame.City, t.HomeGame.State,
            })
            .ToListAsync(ct);
        if (tournaments.Count == 0)
            return 0;

        var approved = JoinRequestService.Approved;
        var sentTotal = 0;
        var ownerPlans = new Dictionary<Guid, string>();
        foreach (var t in tournaments)
        {
            ct.ThrowIfCancellationRequested();
            var registered = (await _db.TournamentEntries.AsNoTracking()
                .Where(e => e.TournamentId == t.Id && e.Person.UserId != null)
                .Select(e => e.Person.UserId!.Value)
                .Distinct()
                .ToListAsync(ct)).ToHashSet();
            var reserved = await _db.TournamentJoinRequests.AsNoTracking()
                .Where(r => r.TournamentId == t.Id && r.Status == approved && r.EntryId == null)
                .Select(r => r.UserId)
                .Distinct()
                .ToListAsync(ct);
            var already = (await _db.TournamentReminders.AsNoTracking()
                .Where(r => r.TournamentId == t.Id)
                .Select(r => r.UserId)
                .ToListAsync(ct)).ToHashSet();
            var recipients = registered.Union(reserved).Where(u => !already.Contains(u)).Distinct().ToList();
            if (recipients.Count == 0)
                continue;

            if (!ownerPlans.TryGetValue(t.OwnerId, out var plan))
                ownerPlans[t.OwnerId] = plan = await _plans.GetUserEffectivePlanAsync(t.OwnerId, ct);
            var club = HomeGamePrimary.LabelFor(t.ClubIsPrimary, plan, t.ClubName, t.OwnerName);
            var when = t.Date.ToOffset(Brt).ToString("HH:mm");
            var where = JoinRequestService.LocationSummary(t.District, t.City, t.State);
            var body = where is null ? $"{club} · {when}. Boa sorte!" : $"{club} · {when} · {where}. Boa sorte!";
            foreach (var userId in recipients)
            {
                await _notifications.StageAsync(
                    [userId],
                    NotificationKind.TournamentReminder,
                    $"Hoje tem {t.Name}",
                    body,
                    registered.Contains(userId) ? TournamentNotifier.MyTournamentLink(t.Id) : JoinRequestService.UpcomingLink,
                    ct);
                _db.TournamentReminders.Add(new TournamentReminder { TournamentId = t.Id, UserId = userId, SentAt = now });
            }
            sentTotal += recipients.Count;
        }
        if (sentTotal == 0)
            return 0;

        await _db.SaveChangesAsync(ct);
        await _notifications.PushPendingAsync(ct);
        _logger.LogInformation("Lembrete do dia: {Count} conta(s) avisada(s)", sentTotal);
        return sentTotal;
    }
}
