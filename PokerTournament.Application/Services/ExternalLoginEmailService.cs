using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerTournament.Application.Common;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Services;

/// <summary>
/// Envia o aviso "O Google foi conectado à sua conta" (SPEC-20261023-1210) em segundo plano, com escopo de DI
/// próprio, e registra em notification_logs (template "external_login_linked", só o e-mail mascarado) — mesmo
/// molde do <see cref="EmailConfirmationService"/>. Falha no envio nunca sobe para o login.
/// </summary>
public class ExternalLoginEmailService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PublicAppOptions _publicApp;
    private readonly ILogger<ExternalLoginEmailService> _logger;

    public ExternalLoginEmailService(
        IServiceScopeFactory scopes, IOptions<PublicAppOptions> publicApp, ILogger<ExternalLoginEmailService> logger)
    {
        _scopes = scopes;
        _publicApp = publicApp.Value;
        _logger = logger;
    }

    /// <summary>Chamar só DEPOIS do commit do vínculo.</summary>
    public void QueueLinkedNotice(User user, ExternalProviderDefinition provider)
    {
        var message = ExternalLoginLinkedEmail.Build(
            user.Email,
            provider.DisplayName,
            provider.ConnectedTitle,
            _publicApp.BuildUrl("esqueci-senha"),
            hasPassword: user.PasswordHash is not null,
            _publicApp.BuildUrl("brand/logo-completa.png"));
        // Fire-and-forget: o login responde agora; o envio usa escopo próprio (o da requisição morre com ela)
        _ = Task.Run(() => SendInBackgroundAsync(message));
    }

    private async Task SendInBackgroundAsync(EmailMessage message)
    {
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
                    TemplateKey = ExternalLoginLinkedEmail.TemplateKey,
                    Status = result.Sent ? "Sent" : "Failed",
                    SentAt = result.Sent ? DateTimeOffset.UtcNow : null,
                    ErrorMessage = result.Error,
                    Message = PasswordResetEmail.Mask(message.To),
                });
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível registrar o aviso de login conectado em notification_logs");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no envio do aviso de login conectado para {Email}", PasswordResetEmail.Mask(message.To));
        }
    }
}
