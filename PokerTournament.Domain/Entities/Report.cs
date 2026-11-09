using PokerTournament.Domain.Enums;

namespace PokerTournament.Domain.Entities;

/// <summary>
/// Denúncia (SPEC-20261108-1638, D11): quem denunciou, a conta denunciada, a conversa e, se foi uma mensagem específica,
/// a mensagem. <see cref="ReportedText"/> é a CÓPIA do texto no momento da denúncia (a mensagem pode ser apagada depois
/// pelo autor; a Administração continua vendo o que foi denunciado). <see cref="Reason"/> é <see cref="ReportReason"/> e
/// <see cref="Status"/> é <see cref="ReportStatus"/>, via nameof. Não é soft-deletable: denúncia não se apaga.
/// </summary>
public class Report : BaseEntity
{
    public const int DetailsMaxLength = 500;
    public const int ReportedTextMaxLength = 4000;

    public Guid ReporterId { get; set; }
    public Guid ReportedUserId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public string Reason { get; set; } = nameof(ReportReason.Other);
    /// <summary>O que a pessoa escreveu ao denunciar (até 500).</summary>
    public string? Details { get; set; }
    /// <summary>Cópia do texto denunciado (a mensagem, ou as últimas mensagens do denunciado na conversa).</summary>
    public string? ReportedText { get; set; }
    public string Status { get; set; } = nameof(ReportStatus.Open);
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    // Navigation
    public User Reporter { get; set; } = null!;
    public User ReportedUser { get; set; } = null!;
    public Conversation? Conversation { get; set; }
    public ChatMessage? Message { get; set; }
    public User? ReviewedBy { get; set; }
}
