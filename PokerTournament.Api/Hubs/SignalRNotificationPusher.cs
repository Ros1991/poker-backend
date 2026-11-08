using Microsoft.AspNetCore.SignalR;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Api.Hubs;

/// <summary>
/// Entrega da notificação em tempo real (SPEC-20261108-1635): evento <c>NotificationCreated</c> para o grupo
/// <c>user_{id}</c> do <see cref="TournamentHub"/>, em que toda conexão com token de usuário entra ao conectar. O token de
/// dealer não entra no grupo, então nunca recebe. Singleton: só depende do IHubContext.
/// </summary>
public sealed class SignalRNotificationPusher : INotificationPusher
{
    private readonly IHubContext<TournamentHub> _hub;

    public SignalRNotificationPusher(IHubContext<TournamentHub> hub)
    {
        _hub = hub;
    }

    public Task PushAsync(Guid userId, NotificationPush notification, CancellationToken ct = default) =>
        _hub.Clients.Group(NotificationGroups.ForUser(userId)).SendAsync(NotificationGroups.CreatedEvent, notification, ct);
}
