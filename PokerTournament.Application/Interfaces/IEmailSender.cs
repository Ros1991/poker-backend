namespace PokerTournament.Application.Interfaces;

/// <summary>E-mail transacional pronto para envio (HTML + versão texto).</summary>
public record EmailMessage(string To, string Subject, string Html, string Text);

/// <summary>
/// Resultado do envio. <see cref="Sent"/> falso nunca vira exceção para quem chamou:
/// quem pede o envio decide o que fazer (registrar em notification_logs, por exemplo).
/// </summary>
public record EmailSendResult(bool Sent, string? ProviderId, string? Error);

/// <summary>
/// Envio de e-mail. Em produção é o Resend (HTTP); em desenvolvimento ou sem chave,
/// só registra no log. Mesmo espírito do <see cref="IWhatsAppProvider"/>.
/// </summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}
