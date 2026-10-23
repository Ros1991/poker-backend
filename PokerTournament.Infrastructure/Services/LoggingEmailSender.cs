using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Infrastructure.Services;

/// <summary>
/// Desenvolvimento / sem RESEND_APITOKEN: não envia nada, só escreve no log o destinatário,
/// o assunto e o link completo (é assim que se testa o fluxo local sem Resend). Como ninguém
/// recebeu nada, o resultado é Sent=false (notification_logs fica "Failed" com o motivo).
/// ATENÇÃO: o log contém o token puro — nunca usar em produção com chave configurada.
/// </summary>
public partial class LoggingEmailSender : IEmailSender
{
    public const string NotSentError =
        "Nenhum provedor de e-mail configurado (RESEND_APITOKEN ausente): link só no log do servidor.";

    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var links = LinkRegex().Matches(message.Text).Select(m => m.Value).Distinct().ToList();
        _logger.LogInformation(
            "[E-MAIL NÃO ENVIADO — só no log] Para: {To} | Assunto: {Subject} | Link: {Link}",
            message.To, message.Subject, links.Count > 0 ? string.Join(" ", links) : "(sem link)");
        return Task.FromResult(new EmailSendResult(false, "log", NotSentError));
    }

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex LinkRegex();
}
