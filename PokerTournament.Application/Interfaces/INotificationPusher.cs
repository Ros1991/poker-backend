namespace PokerTournament.Application.Interfaces;

/// <summary>
/// O que a aba aberta recebe no evento <see cref="NotificationGroups.CreatedEvent"/> (SPEC-20261108-1635): a notificação
/// nova e quantas não lidas a conta tem depois dela (o badge do sino não precisa consultar de novo).
/// </summary>
public record NotificationPush(
    Guid Id,
    string Kind,
    string Title,
    string Body,
    string? Link,
    DateTimeOffset CreatedAt,
    int UnreadCount);

/// <summary>
/// Empurra uma notificação para as conexões da conta em tempo real. A implementação fica na API (SignalR, grupo
/// <c>user_{id}</c> do TournamentHub): a camada Application não conhece o hub, como nos demais serviços — aqui a abstração
/// existe porque a notificação nasce dentro de fluxos de serviço (início/fim do torneio), e não num controller só.
/// </summary>
public interface INotificationPusher
{
    Task PushAsync(Guid userId, NotificationPush notification, CancellationToken ct = default);
}

/// <summary>Nomes do grupo por conta e do evento, compartilhados pelo hub e pelo empurrador.</summary>
public static class NotificationGroups
{
    public const string CreatedEvent = "NotificationCreated";

    /// <summary>Grupo do hub em que toda conexão com token de usuário entra ao conectar (token de dealer não entra).</summary>
    public static string ForUser(Guid userId) => $"user_{userId}";
}
