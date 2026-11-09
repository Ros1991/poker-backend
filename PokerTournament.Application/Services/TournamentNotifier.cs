using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Application.Services;

/// <summary>
/// Notificações do torneio para as contas vinculadas (SPEC-20261108-1635): "começou" (<see cref="NotificationKind.TournamentStarted"/>)
/// e o resultado (<see cref="NotificationKind.ResultPublished"/>, ou <see cref="NotificationKind.TournamentFinished"/> para quem
/// não tem posição). Conta vinculada = inscrição cujo jogador tem <c>Person.UserId</c> (SPEC-20261029-1725), uma notificação
/// por conta mesmo com re-entradas. Os <c>Stage*</c> só preparam (o fluxo que chama salva — botão do painel, relógio ou
/// eliminação final); <see cref="PushPendingAsync"/> vai depois do SaveChanges/commit. Os links levam à área do jogador
/// (<c>/meus-torneios/{id}</c>).
/// </summary>
public class TournamentNotifier
{
    private static readonly NumberFormatInfo PtBrNumber = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberDecimalDigits = 2,
    };

    private readonly IAppDbContext _db;
    private readonly NotificationService _notifications;

    public TournamentNotifier(IAppDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public static string MyTournamentLink(Guid tournamentId) => $"/meus-torneios/{tournamentId}";

    /// <summary>
    /// O torneio passou a Em andamento (de rascunho ou inscrições abertas): avisa cada conta vinculada — e, desde a
    /// SPEC-20261108-1639, também quem tem reserva aprovada ainda não inscrita (chegou e vai entrar).
    /// </summary>
    public async Task StageStartedAsync(Tournament tournament, CancellationToken ct = default)
    {
        var userIds = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournament.Id && e.Person.UserId != null)
            .Select(e => e.Person.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);
        var approved = nameof(JoinRequestStatus.Approved);
        var reserved = await _db.TournamentJoinRequests
            .Where(r => r.TournamentId == tournament.Id && r.Status == approved && r.EntryId == null)
            .Select(r => r.UserId)
            .Distinct()
            .ToListAsync(ct);
        await _notifications.StageAsync(
            userIds.Concat(reserved),
            NotificationKind.TournamentStarted,
            $"{tournament.Name} começou",
            "Boa sorte! Acompanhe o relógio e a sua ficha ao vivo.",
            MyTournamentLink(tournament.Id),
            ct);
    }

    /// <summary>
    /// O torneio terminou (finalização manual ou automática): posição e prêmio para cada conta vinculada. As inscrições
    /// vêm rastreadas — a posição e o prêmio recém-atribuídos no mesmo fluxo (campeão da última eliminação) valem.
    /// </summary>
    public async Task StageResultAsync(Tournament tournament, CancellationToken ct = default)
    {
        var entries = await _db.TournamentEntries
            .Include(e => e.Person)
            .Where(e => e.TournamentId == tournament.Id && e.Person.UserId != null)
            .ToListAsync(ct);

        foreach (var group in entries.GroupBy(e => e.Person.UserId!.Value))
        {
            var placed = group.Where(e => e.FinalPosition is > 0).OrderBy(e => e.FinalPosition).FirstOrDefault();
            if (placed is null)
            {
                await _notifications.StageAsync(
                    [group.Key],
                    NotificationKind.TournamentFinished,
                    $"{tournament.Name} terminou",
                    "Veja o resultado e a sua ficha.",
                    MyTournamentLink(tournament.Id),
                    ct);
                continue;
            }

            var prize = placed.PrizeAmount is > 0 ? placed.PrizeAmount.Value : 0m;
            var body = prize > 0
                ? $"Você terminou em {placed.FinalPosition}º lugar e levou R$ {prize.ToString("N2", PtBrNumber)}."
                : $"Você terminou em {placed.FinalPosition}º lugar.";
            await _notifications.StageAsync(
                [group.Key],
                NotificationKind.ResultPublished,
                $"Resultado: {tournament.Name}",
                body,
                MyTournamentLink(tournament.Id),
                ct);
        }
    }

    /// <summary>Empurra em tempo real o que ficou preparado — depois do SaveChanges (e do commit) de quem chamou.</summary>
    public Task PushPendingAsync(CancellationToken ct = default) => _notifications.PushPendingAsync(ct);
}
