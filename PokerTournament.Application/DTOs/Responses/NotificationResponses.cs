namespace PokerTournament.Application.DTOs.Responses;

/// <summary>Um item do sino (SPEC-20261108-1635).</summary>
public class NotificationResponse
{
    public Guid Id { get; set; }
    /// <summary>Nome do <see cref="Domain.Enums.NotificationKind"/>.</summary>
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>Caminho relativo do site (null = só informativo).</summary>
    public string? Link { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary><c>GET me/notifications</c>: as 20 últimas e o total de não lidas.</summary>
public class NotificationListResponse
{
    public List<NotificationResponse> Items { get; set; } = [];
    public int UnreadCount { get; set; }
}

/// <summary><c>POST me/notifications/{id}/read</c> e <c>read-all</c>: quantas ficaram não lidas (e quantas foram marcadas agora).</summary>
public class NotificationReadResponse
{
    public int UnreadCount { get; set; }
    public int Marked { get; set; }
}

/// <summary><c>GET/PUT me/notification-preferences</c>.</summary>
public class NotificationPreferencesResponse
{
    /// <summary>Receber o e-mail resumo (no máximo um por hora, só com não lidas).</summary>
    public bool NotifyByEmail { get; set; }
    /// <summary>Torneio começou, resultado, convite.</summary>
    public bool NotifyTournament { get; set; }
    /// <summary>Pedidos de vínculo e avisos do clube.</summary>
    public bool NotifyRequests { get; set; }
    /// <summary>Amigos e mensagens (SPECs seguintes).</summary>
    public bool NotifySocial { get; set; }
}
