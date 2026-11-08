using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerTournament.Application.Common;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Services;

/// <summary>Um e-mail resumo produzido numa rodada (o HTML só é devolvido ao endpoint de desenvolvimento).</summary>
public record DigestEmailInfo(Guid UserId, string MaskedTo, string Subject, int Items, bool Sent, string? Error, string Html);

public record DigestRunResult(int Sent, int Failed, List<DigestEmailInfo> Emails);

/// <summary>
/// E-mail resumo das notificações (SPEC-20261108-1635, D10) e limpeza das lidas antigas. O hosted service
/// (<c>NotificationDigestJob</c>, a cada 10 min) chama <see cref="RunAsync"/> e <see cref="CleanupAsync"/> com o relógio
/// de verdade; o endpoint de desenvolvimento chama com um relógio falso.
/// <para>
/// Regra por conta: recebe e-mail quem está com <c>NotifyByEmail</c>, confirmada e ativa (<see cref="PlayerLinkService.Linkable"/>),
/// sem resumo na última hora (<c>LastDigestAt</c>) e com alguma não lida criada há mais de 5 min que ainda não foi por
/// e-mail (<c>EmailedAt</c>). O e-mail leva até 10 não lidas ainda não enviadas (mais recentes primeiro) e o total de não
/// lidas. Enviou → as 10 ganham <c>EmailedAt</c> (não se repetem na próxima hora); falhou → ficam para a próxima rodada.
/// <c>LastDigestAt</c> é gravado nos dois casos. Cada envio vira uma linha em <c>notification_logs</c> (destinatário
/// mascarado, nunca o conteúdo), como os e-mails de conta.
/// </para>
/// </summary>
public class NotificationDigestService
{
    public static readonly TimeSpan MinAge = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan ReadRetention = TimeSpan.FromDays(90);
    public const int MaxItems = 10;

    private readonly IAppDbContext _db;
    private readonly IEmailSender _sender;
    private readonly PublicAppOptions _publicApp;
    private readonly ILogger<NotificationDigestService> _logger;

    public NotificationDigestService(
        IAppDbContext db, IEmailSender sender, IOptions<PublicAppOptions> publicApp, ILogger<NotificationDigestService> logger)
    {
        _db = db;
        _sender = sender;
        _publicApp = publicApp.Value;
        _logger = logger;
    }

    public async Task<DigestRunResult> RunAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var minCreated = now - MinAge;
        var since = now - MinInterval;

        var users = await PlayerLinkService.Linkable(_db.Users)
            .Where(u => u.NotifyByEmail
                && (u.LastDigestAt == null || u.LastDigestAt <= since)
                && _db.Notifications.Any(n => n.UserId == u.Id && n.ReadAt == null && n.EmailedAt == null && n.CreatedAt <= minCreated))
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(ct);

        var sent = 0;
        var failed = 0;
        var emails = new List<DigestEmailInfo>();
        var logoUrl = _publicApp.BuildUrl("brand/logo-completa.png");

        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();
            var items = await _db.Notifications
                .Where(n => n.UserId == user.Id && n.ReadAt == null && n.EmailedAt == null)
                .OrderByDescending(n => n.CreatedAt)
                .Take(MaxItems)
                .ToListAsync(ct);
            if (items.Count == 0)
                continue;
            var totalUnread = await _db.Notifications.CountAsync(n => n.UserId == user.Id && n.ReadAt == null, ct);

            var message = NotificationDigestEmail.Build(
                user.Email,
                items.Select(n => new DigestItem(n.Title, n.Body, n.Link is null ? null : _publicApp.BuildUrl(n.Link), n.CreatedAt)).ToList(),
                totalUnread,
                _publicApp.Url,
                logoUrl);

            EmailSendResult result;
            try
            {
                result = await _sender.SendAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha no envio do e-mail resumo para a conta {UserId}", user.Id);
                result = new EmailSendResult(false, null, ex.Message);
            }

            user.LastDigestAt = now;
            if (result.Sent)
            {
                sent++;
                foreach (var item in items)
                    item.EmailedAt = now;
            }
            else
            {
                failed++;
            }

            var masked = PasswordResetEmail.Mask(user.Email);
            _db.NotificationLogs.Add(new NotificationLog
            {
                Channel = "Email",
                TemplateKey = NotificationDigestEmail.TemplateKey,
                Status = result.Sent ? "Sent" : "Failed",
                SentAt = result.Sent ? now : null,
                ErrorMessage = result.Error,
                Message = masked,
            });
            await _db.SaveChangesAsync(ct);

            emails.Add(new DigestEmailInfo(user.Id, masked, message.Subject, items.Count, result.Sent, result.Error, message.Html));
        }

        if (sent > 0 || failed > 0)
            _logger.LogInformation("E-mail resumo de notificações: {Sent} enviado(s), {Failed} com falha", sent, failed);
        return new DigestRunResult(sent, failed, emails);
    }

    /// <summary>Apaga de verdade as notificações lidas há mais de 90 dias. Devolve quantas.</summary>
    public Task<int> CleanupAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var cutoff = now - ReadRetention;
        return _db.Notifications
            .Where(n => n.ReadAt != null && n.ReadAt <= cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
