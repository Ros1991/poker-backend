using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerTournament.Application.Common;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Services;

/// <summary>
/// E-mails da página "Meu perfil" (SPEC-20261023-1211): hoje só o "Definir sua senha" da conta sem senha. Envia em
/// segundo plano, com escopo de DI próprio, e registra em notification_logs (só o e-mail mascarado, nunca o link) —
/// mesmo molde do <see cref="ExternalLoginEmailService"/>. Falha no envio nunca sobe para a requisição.
/// </summary>
public class AccountEmailService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PublicAppOptions _publicApp;
    private readonly ILogger<AccountEmailService> _logger;

    public AccountEmailService(
        IServiceScopeFactory scopes, IOptions<PublicAppOptions> publicApp, ILogger<AccountEmailService> logger)
    {
        _scopes = scopes;
        _publicApp = publicApp.Value;
        _logger = logger;
    }

    /// <summary>Chamar só DEPOIS de o token do link estar gravado.</summary>
    public void QueuePasswordSetup(string email, string link)
    {
        var message = PasswordSetupEmail.Build(email, link, _publicApp.BuildUrl("brand/logo-completa.png"));
        // Fire-and-forget: a resposta 202 sai agora; o envio usa escopo próprio (o da requisição morre com ela)
        _ = Task.Run(() => SendInBackgroundAsync(message, PasswordSetupEmail.TemplateKey));
    }

    private async Task SendInBackgroundAsync(EmailMessage message, string templateKey)
    {
        var masked = PasswordResetEmail.Mask(message.To);
        try
        {
            using var scope = _scopes.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            var result = await sender.SendAsync(message, CancellationToken.None);
            try
            {
                db.NotificationLogs.Add(new NotificationLog
                {
                    Channel = "Email",
                    TemplateKey = templateKey,
                    Status = result.Sent ? "Sent" : "Failed",
                    SentAt = result.Sent ? DateTimeOffset.UtcNow : null,
                    ErrorMessage = result.Error,
                    Message = masked,
                });
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível registrar o e-mail {Template} em notification_logs", templateKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no envio do e-mail {Template} para {Email}", templateKey, masked);
        }
    }
}
