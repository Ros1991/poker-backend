namespace PokerTournament.Application.DTOs.Requests;

// Mensagens 1 para 1 e denúncias (SPEC-20261108-1638).

/// <summary>
/// <c>POST conversations</c>: abre (ou devolve) a conversa direta com <see cref="UserId"/>, ou a conversa do pedido de vaga
/// <see cref="TournamentRequestId"/> (SPEC-20261108-1639). Exatamente um dos dois.
/// </summary>
public class OpenConversationRequest
{
    public Guid? UserId { get; set; }
    public Guid? TournamentRequestId { get; set; }
}

/// <summary><c>POST conversations/{id}/messages</c>: texto puro, 1 a 2.000 caracteres.</summary>
public class SendMessageRequest
{
    public string Body { get; set; } = string.Empty;
}

/// <summary>
/// <c>POST reports</c>: denuncia uma mensagem (<see cref="MessageId"/>) ou uma conversa inteira (<see cref="ConversationId"/>)
/// com um motivo da lista fixa (<c>Domain.Enums.ReportReason</c>) e detalhes opcionais (até 500).
/// </summary>
public class CreateReportRequest
{
    public Guid? ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Details { get; set; }
}
