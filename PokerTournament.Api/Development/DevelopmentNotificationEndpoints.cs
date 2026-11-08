using PokerTournament.Application.Services;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Api.Development;

/// <summary>
/// Testes das notificações (SPEC-20261108-1635) — rotas SÓ em Development (o Program.cs só as mapeia lá) e só para o
/// Admin, no molde do <see cref="DevelopmentAiEndpoints"/>: criar uma notificação para qualquer conta (confere o grupo
/// <c>user_{id}</c> do hub) e rodar o e-mail resumo com um relógio falso (o hosted service só roda a cada 10 min e a regra
/// é de 5 min / 1 h). O HTML do e-mail volta na resposta para a suíte conferir a marca e os links.
/// </summary>
public static class DevelopmentNotificationEndpoints
{
    public sealed class DigestRequest
    {
        /// <summary>Relógio falso (vazio = agora).</summary>
        public DateTimeOffset? Now { get; set; }
    }

    public sealed class NotifyRequest
    {
        public Guid UserId { get; set; }
        public string Kind { get; set; } = nameof(NotificationKind.TournamentStarted);
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? Link { get; set; }
    }

    public static void MapDevelopmentNotificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/dev/notifications")
            .RequireAuthorization("Admin")
            .WithTags("Notificações (somente Development)");

        group.MapPost("/", async (NotifyRequest request, NotificationService notifications, CancellationToken ct) =>
        {
            if (!Enum.TryParse<NotificationKind>(request.Kind, out var kind))
                return Results.BadRequest(new { message = $"Tipo inválido: {request.Kind}" });
            var created = await notifications.NotifyAsync(request.UserId, kind, request.Title, request.Body, request.Link, ct);
            return Results.Ok(new { created = created is not null, id = created?.Id, createdAt = created?.CreatedAt });
        });

        group.MapPost("/digest", async (DigestRequest? request, NotificationDigestService digest, CancellationToken ct) =>
        {
            var now = request?.Now ?? DateTimeOffset.UtcNow;
            var result = await digest.RunAsync(now, ct);
            var cleaned = await digest.CleanupAsync(now, ct);
            return Results.Ok(new { now, sent = result.Sent, failed = result.Failed, cleaned, emails = result.Emails });
        });
    }
}
