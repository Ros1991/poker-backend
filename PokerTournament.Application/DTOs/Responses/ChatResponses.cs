namespace PokerTournament.Application.DTOs.Responses;

// Mensagens 1 para 1 e denúncias (SPEC-20261108-1638). MESMA REGRA do perfil público: para a outra pessoa da conversa saem
// só id, handle, apelido (ou primeiro nome) e foto — nunca nome completo, e-mail ou WhatsApp. A Administração
// (AdminReportResponse) é a exceção: vê o nome completo de quem denunciou e do denunciado.

/// <summary>A outra conta de uma conversa.</summary>
public class ChatUserResponse
{
    public Guid Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    /// <summary>Apelido; sem apelido, o primeiro nome. Conta excluída: "Conta excluída".</summary>
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    /// <summary>Falso para conta desativada ou excluída (a conversa continua legível, mas não dá para mandar mais).</summary>
    public bool IsActive { get; set; }
}

/// <summary>Uma mensagem. Apagada pelo autor: <see cref="IsDeleted"/> verdadeiro e <see cref="Body"/> nulo ("mensagem apagada").</summary>
public class MessageResponse
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Uma conversa da conta (<c>GET me/conversations</c>, <c>GET conversations/{id}</c>).</summary>
public class ConversationResponse
{
    public Guid Id { get; set; }
    /// <summary><c>Direct</c> ou <c>TournamentRequest</c>.</summary>
    public string Type { get; set; } = string.Empty;
    public Guid? TournamentId { get; set; }
    public string? TournamentName { get; set; }
    public ChatUserResponse Other { get; set; } = new();
    public MessageResponse? LastMessage { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    /// <summary>Mensagens da outra conta depois de <see cref="MyLastReadAt"/>.</summary>
    public int UnreadCount { get; set; }
    public DateTimeOffset? MyLastReadAt { get; set; }
    /// <summary>Até quando a outra conta leu (o "lida" das suas mensagens).</summary>
    public DateTimeOffset? OtherLastReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>POST conversations</c>: a conversa (nova ou a que já existia).</summary>
public class OpenConversationResponse
{
    public ConversationResponse Conversation { get; set; } = new();
    /// <summary>Verdadeiro quando a conversa acabou de ser criada.</summary>
    public bool Created { get; set; }
}

/// <summary><c>GET me/conversations/with/{userId}</c>: quem vê pode abrir conversa com a conta (e a conversa, se já existe).</summary>
public class CanMessageResponse
{
    public bool CanOpen { get; set; }
    public Guid? ConversationId { get; set; }
    /// <summary>Por que não pode (privacidade de quem recebe); null quando pode.</summary>
    public string? Reason { get; set; }
}

/// <summary><c>GET conversations/{id}/messages?before=&amp;limit=</c>: em ordem cronológica; <see cref="HasMore"/> = há mais antigas.</summary>
public class MessagePageResponse
{
    public List<MessageResponse> Items { get; set; } = [];
    public bool HasMore { get; set; }
}

/// <summary><c>POST conversations/{id}/read</c>.</summary>
public class ConversationReadResponse
{
    public DateTimeOffset ReadAt { get; set; }
    /// <summary>Quantas conversas da conta ainda têm mensagem não lida.</summary>
    public int UnreadConversations { get; set; }
}

/// <summary><c>GET me/conversations/unread-count</c>: conversas com mensagem depois de <c>last_read_at</c>.</summary>
public class UnreadConversationsResponse
{
    public int Count { get; set; }
}

/// <summary><c>POST reports</c>.</summary>
public class ReportResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Uma conta na denúncia (Administração: nome completo, para a ficha).</summary>
public class AdminReportUserResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public bool IsActive { get; set; }
}

/// <summary><c>GET admin/reports</c> e <c>POST admin/reports/{id}/review</c>.</summary>
public class AdminReportResponse
{
    public Guid Id { get; set; }
    /// <summary><c>Domain.Enums.ReportReason</c>.</summary>
    public string Reason { get; set; } = string.Empty;
    public string? Details { get; set; }
    /// <summary>Cópia do texto denunciado no momento da denúncia.</summary>
    public string? ReportedText { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    /// <summary><c>Open</c> ou <c>Reviewed</c>.</summary>
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedByName { get; set; }
    public AdminReportUserResponse Reporter { get; set; } = new();
    public AdminReportUserResponse Reported { get; set; } = new();
}
