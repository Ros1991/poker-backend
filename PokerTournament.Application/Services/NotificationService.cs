using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Application.Services;

/// <summary>
/// Notificações da conta (SPEC-20261108-1635): grava em <c>notifications</c>, empurra em tempo real para o grupo
/// <c>user_{id}</c> (via <see cref="INotificationPusher"/>) e deixa o e-mail resumo para o <see cref="NotificationDigestService"/>.
/// <para>
/// Dois jeitos de usar. (1) <see cref="NotifyAsync"/>/<see cref="NotifyManyAsync"/>: grava, salva e empurra — para quem
/// chama fora de um fluxo com SaveChanges próprio (pedido de vínculo, convite, aviso de endereço…). Atenção: salvam o
/// contexto escopado inteiro. (2) <see cref="StageAsync"/> + <see cref="PushPendingAsync"/>: dentro de um fluxo que já vai
/// salvar (início/fim do torneio), prepara as linhas no contexto; depois do SaveChanges (e do commit) de quem chama,
/// empurra o que ficou preparado — nada sai em tempo real antes de estar gravado.
/// </para>
/// A categoria do tipo (<see cref="CategoryOf"/>) respeita a preferência da conta: categoria desligada não cria a
/// notificação (nem sino, nem e-mail). Conta inativa também não recebe. Escopado por requisição (a lista de pendentes é
/// da requisição).
/// </summary>
public class NotificationService
{
    public const int ListSize = 20;
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 500;
    public const int LinkMaxLength = 500;

    private readonly IAppDbContext _db;
    private readonly INotificationPusher _pusher;
    private readonly ILogger<NotificationService> _logger;
    private readonly List<Notification> _pending = [];

    public NotificationService(IAppDbContext db, INotificationPusher pusher, ILogger<NotificationService> logger)
    {
        _db = db;
        _pusher = pusher;
        _logger = logger;
    }

    /// <summary>Categoria de preferência de cada tipo (Torneio / Pedidos e avisos / Social).</summary>
    public static NotificationCategory CategoryOf(NotificationKind kind) => kind switch
    {
        NotificationKind.TournamentStarted or NotificationKind.TournamentFinished
            or NotificationKind.ResultPublished or NotificationKind.TournamentInvite
            // Pedido de vaga (SPEC-20261108-1639): a resposta ao interessado e o lembrete do dia são "do torneio"
            or NotificationKind.JoinRequestApproved or NotificationKind.JoinRequestDeclined
            or NotificationKind.JoinRequestWaitlisted or NotificationKind.JoinRequestPromoted
            or NotificationKind.TournamentReminder => NotificationCategory.Tournament,
        NotificationKind.PlayerLinkRequested or NotificationKind.PlayerLinkAnswered
            or NotificationKind.AddressIncomplete or NotificationKind.JoinRequestReceived => NotificationCategory.Requests,
        _ => NotificationCategory.Social,
    };

    // ---------------------------------------------------------------- criar

    /// <summary>
    /// Prepara uma notificação por conta (sem salvar): adiciona ao contexto e guarda para o <see cref="PushPendingAsync"/>.
    /// Contas repetidas, inativas ou com a categoria desligada ficam de fora. Devolve as criadas.
    /// </summary>
    public async Task<List<Notification>> StageAsync(
        IEnumerable<Guid> userIds, NotificationKind kind, string title, string body, string? link, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var category = CategoryOf(kind);
        var recipients = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.NotifyTournament, u.NotifyRequests, u.NotifySocial })
            .ToListAsync(ct);

        var created = new List<Notification>();
        foreach (var recipient in recipients)
        {
            var wants = category switch
            {
                NotificationCategory.Tournament => recipient.NotifyTournament,
                NotificationCategory.Requests => recipient.NotifyRequests,
                _ => recipient.NotifySocial,
            };
            if (!wants)
                continue;

            var notification = new Notification
            {
                UserId = recipient.Id,
                Kind = kind.ToString(),
                Title = Truncate(title, TitleMaxLength),
                Body = Truncate(body, BodyMaxLength),
                Link = string.IsNullOrWhiteSpace(link) ? null : Truncate(link, LinkMaxLength),
            };
            _db.Notifications.Add(notification);
            _pending.Add(notification);
            created.Add(notification);
        }
        return created;
    }

    /// <summary>
    /// Empurra em tempo real o que foi preparado por <see cref="StageAsync"/>. Chamar DEPOIS do SaveChanges (e do commit)
    /// de quem chama: o id e o created_at nascem no SaveChanges. Falha no envio só vai para o log — a gravação já valeu.
    /// </summary>
    public async Task PushPendingAsync(CancellationToken ct = default)
    {
        if (_pending.Count == 0)
            return;
        var batch = _pending.ToList();
        _pending.Clear();

        foreach (var group in batch.GroupBy(n => n.UserId))
        {
            int unread;
            try
            {
                unread = await CountUnreadAsync(group.Key, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não consegui contar as não lidas da conta {UserId}", group.Key);
                unread = group.Count();
            }

            foreach (var notification in group.OrderBy(n => n.CreatedAt))
            {
                try
                {
                    await _pusher.PushAsync(notification.UserId, new NotificationPush(
                        notification.Id, notification.Kind, notification.Title, notification.Body, notification.Link,
                        notification.CreatedAt, unread), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao empurrar a notificação {Id} para a conta {UserId}", notification.Id, notification.UserId);
                }
            }
        }
    }

    /// <summary>Grava, salva e empurra para uma conta. Devolve null se a conta não recebe esse tipo.</summary>
    public async Task<Notification?> NotifyAsync(
        Guid userId, NotificationKind kind, string title, string body, string? link = null, CancellationToken ct = default)
    {
        var created = await StageAsync([userId], kind, title, body, link, ct);
        if (created.Count == 0)
            return null;
        await _db.SaveChangesAsync(ct);
        await PushPendingAsync(ct);
        return created[0];
    }

    /// <summary>Grava, salva e empurra para várias contas. Devolve quantas receberam.</summary>
    public async Task<int> NotifyManyAsync(
        IEnumerable<Guid> userIds, NotificationKind kind, string title, string body, string? link = null, CancellationToken ct = default)
    {
        var created = await StageAsync(userIds, kind, title, body, link, ct);
        if (created.Count == 0)
            return 0;
        await _db.SaveChangesAsync(ct);
        await PushPendingAsync(ct);
        return created.Count;
    }

    // ---------------------------------------------------------------- ler

    /// <summary>As 20 últimas da conta (mais recente primeiro) e o total de não lidas.</summary>
    public async Task<NotificationListResponse> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var items = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(ListSize)
            .Select(n => new NotificationResponse
            {
                Id = n.Id,
                Kind = n.Kind,
                Title = n.Title,
                Body = n.Body,
                Link = n.Link,
                CreatedAt = n.CreatedAt,
                ReadAt = n.ReadAt,
            })
            .ToListAsync(ct);
        return new NotificationListResponse { Items = items, UnreadCount = await CountUnreadAsync(userId, ct) };
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default) =>
        _db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);

    /// <summary>Marca uma notificação da conta como lida (idempotente). Null = não é desta conta ou não existe.</summary>
    public async Task<NotificationReadResponse?> MarkReadAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
        if (notification is null)
            return null;
        var marked = 0;
        if (notification.ReadAt is null)
        {
            notification.ReadAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            marked = 1;
        }
        return new NotificationReadResponse { UnreadCount = await CountUnreadAsync(userId, ct), Marked = marked };
    }

    /// <summary>Marca todas as não lidas da conta. UPDATE direto (sem carregar as linhas).</summary>
    public async Task<NotificationReadResponse> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var marked = await _db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(n => n.ReadAt, now)
                .SetProperty(n => n.UpdatedAt, now), ct);
        return new NotificationReadResponse { UnreadCount = 0, Marked = marked };
    }

    // ---------------------------------------------------------------- preferências

    public async Task<NotificationPreferencesResponse?> GetPreferencesAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? null : ToPreferences(user);
    }

    /// <summary>Campo ausente não mexe. Devolve null se a conta não existe.</summary>
    public async Task<NotificationPreferencesResponse?> UpdatePreferencesAsync(
        Guid userId, UpdateNotificationPreferencesRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return null;
        if (request.NotifyByEmail is { } byEmail) user.NotifyByEmail = byEmail;
        if (request.NotifyTournament is { } tournament) user.NotifyTournament = tournament;
        if (request.NotifyRequests is { } requests) user.NotifyRequests = requests;
        if (request.NotifySocial is { } social) user.NotifySocial = social;
        await _db.SaveChangesAsync(ct);
        return ToPreferences(user);
    }

    private static NotificationPreferencesResponse ToPreferences(User user) => new()
    {
        NotifyByEmail = user.NotifyByEmail,
        NotifyTournament = user.NotifyTournament,
        NotifyRequests = user.NotifyRequests,
        NotifySocial = user.NotifySocial,
    };

    private static string Truncate(string? value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)] + "…";
    }
}
