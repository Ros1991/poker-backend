using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Denúncias (SPEC-20261108-1638, D11). Quem denuncia precisa ser membro da conversa; a denúncia leva a conta denunciada
/// (quem enviou a mensagem, ou a outra conta da conversa), o motivo da lista fixa, os detalhes e a CÓPIA do texto no
/// momento da denúncia — a mensagem denunciada, ou as últimas mensagens do denunciado na conversa. A Administração lista
/// (<see cref="ListAsync"/>, mais recentes primeiro, filtro por situação) e marca como revisada (<see cref="ReviewAsync"/>,
/// com AuditLog). Ninguém é avisado da denúncia.
/// </summary>
public class ReportService
{
    public const int ConversationSnapshotMessages = 10;
    public const string ReportNotFoundMessage = "Denúncia não encontrada.";
    public const string TargetRequiredMessage = "Informe a mensagem ou a conversa denunciada.";
    public const string InvalidReasonMessage = "Motivo da denúncia inválido.";
    public const string DetailsTooLongMessage = "Os detalhes podem ter no máximo 500 caracteres.";
    public const string OwnMessageMessage = "Você não pode denunciar a sua própria mensagem.";
    public const string ReviewedAction = "ReportReviewed";

    private readonly IAppDbContext _db;

    public ReportService(IAppDbContext db)
    {
        _db = db;
    }

    /// <summary><c>POST reports</c>. Null = conversa/mensagem inexistente ou de outra conta (404); 400 com motivo inválido.</summary>
    public async Task<ReportResponse?> CreateAsync(Guid userId, CreateReportRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<ReportReason>((request.Reason ?? string.Empty).Trim(), ignoreCase: true, out var reason) || !Enum.IsDefined(reason))
            throw new DomainException(InvalidReasonMessage);
        var details = string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim();
        if (details is { Length: > Report.DetailsMaxLength })
            throw new DomainException(DetailsTooLongMessage);
        if (request.MessageId is null && request.ConversationId is null)
            throw new DomainException(TargetRequiredMessage);

        var report = new Report { ReporterId = userId, Reason = reason.ToString(), Details = details };

        if (request.MessageId is { } messageId)
        {
            var message = await _db.Messages.AsNoTracking()
                .Where(m => m.Id == messageId && (m.Conversation.UserLowId == userId || m.Conversation.UserHighId == userId))
                .Select(m => new { m.Id, m.ConversationId, m.SenderId, m.Body, m.SentAt })
                .FirstOrDefaultAsync(ct);
            if (message is null || (request.ConversationId is { } cid && cid != message.ConversationId))
                return null;
            if (message.SenderId == userId)
                throw new DomainException(OwnMessageMessage);
            report.ConversationId = message.ConversationId;
            report.MessageId = message.Id;
            report.ReportedUserId = message.SenderId;
            report.ReportedText = Cut(message.Body, Report.ReportedTextMaxLength);
        }
        else
        {
            var conversation = await _db.Conversations.AsNoTracking()
                .Where(c => c.Id == request.ConversationId && (c.UserLowId == userId || c.UserHighId == userId))
                .Select(c => new { c.Id, c.UserLowId, c.UserHighId })
                .FirstOrDefaultAsync(ct);
            if (conversation is null)
                return null;
            var otherId = conversation.UserLowId == userId ? conversation.UserHighId : conversation.UserLowId;
            report.ConversationId = conversation.Id;
            report.ReportedUserId = otherId;
            var last = await _db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversation.Id && m.SenderId == otherId)
                .OrderByDescending(m => m.SentAt)
                .Take(ConversationSnapshotMessages)
                .Select(m => new { m.Body, m.SentAt })
                .ToListAsync(ct);
            last.Reverse();
            report.ReportedText = last.Count == 0
                ? null
                : Cut(string.Join("\n", last.Select(m => $"[{NotificationDigestEmail.FormatWhen(m.SentAt)}] {m.Body}")), Report.ReportedTextMaxLength);
        }

        _db.Reports.Add(report);
        await _db.SaveChangesAsync(ct);
        return new ReportResponse { Id = report.Id, Status = report.Status, CreatedAt = report.CreatedAt };
    }

    /// <summary><c>GET admin/reports?status=open|reviewed|all&amp;page=&amp;pageSize=</c>: mais recentes primeiro (20 por página, máx. 100).</summary>
    public async Task<PaginatedResponse<AdminReportResponse>> ListAsync(string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var paging = Paging.Normalize(page, pageSize);
        var query = _db.Reports.AsNoTracking();
        var filter = (status ?? "open").Trim().ToLowerInvariant();
        switch (filter)
        {
            case "open":
                query = query.Where(r => r.Status == nameof(ReportStatus.Open));
                break;
            case "reviewed":
                query = query.Where(r => r.Status == nameof(ReportStatus.Reviewed));
                break;
            case "all":
                break;
            default:
                throw new DomainException("Filtro de situação inválido: use open, reviewed ou all.");
        }

        var total = await query.CountAsync(ct);
        if (paging.IsPastEnd(total))
            return new PaginatedResponse<AdminReportResponse>([], paging.Page, paging.PageSize, total);

        var rows = await Project(query
                .OrderByDescending(r => r.CreatedAt)
                .Skip(paging.Skip)
                .Take(paging.PageSize))
            .ToListAsync(ct);
        return new PaginatedResponse<AdminReportResponse>(rows, paging.Page, paging.PageSize, total);
    }

    private static IQueryable<AdminReportResponse> Project(IQueryable<Report> query) =>
        query.Select(r => new AdminReportResponse
        {
            Id = r.Id,
            Reason = r.Reason,
            Details = r.Details,
            ReportedText = r.ReportedText,
            ConversationId = r.ConversationId,
            MessageId = r.MessageId,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            ReviewedAt = r.ReviewedAt,
            ReviewedByName = r.ReviewedBy != null ? r.ReviewedBy.FullName : null,
            Reporter = new AdminReportUserResponse
            {
                Id = r.Reporter.Id,
                FullName = r.Reporter.FullName,
                Handle = r.Reporter.Handle,
                PhotoUrl = r.Reporter.PhotoUrl,
                IsActive = r.Reporter.IsActive,
            },
            Reported = new AdminReportUserResponse
            {
                Id = r.ReportedUser.Id,
                FullName = r.ReportedUser.FullName,
                Handle = r.ReportedUser.Handle,
                PhotoUrl = r.ReportedUser.PhotoUrl,
                IsActive = r.ReportedUser.IsActive,
            },
        });

    /// <summary><c>POST admin/reports/{id}/review</c>: marca como revisada (idempotente) com AuditLog. Null = não existe (404).</summary>
    public async Task<AdminReportResponse?> ReviewAsync(Guid reportId, Guid adminId, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null)
            return null;
        if (report.Status != nameof(ReportStatus.Reviewed))
        {
            var now = DateTimeOffset.UtcNow;
            report.Status = nameof(ReportStatus.Reviewed);
            report.ReviewedById = adminId;
            report.ReviewedAt = now;
            var adminName = await _db.Users.AsNoTracking().Where(u => u.Id == adminId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
            _db.AuditLogs.Add(new AuditLog
            {
                // AuditLog não é BaseEntity: Id e CreatedAt à mão
                Id = Guid.NewGuid(),
                EntityType = "Report",
                EntityId = report.Id,
                Action = ReviewedAction,
                NewValues = $"{{\"status\":\"{nameof(ReportStatus.Reviewed)}\",\"reportedUserId\":\"{report.ReportedUserId}\"}}",
                UserId = adminId,
                UserName = adminName,
                IpAddress = Cut(ip, 50),
                UserAgent = Cut(userAgent, 500),
                CreatedAt = now,
            });
            await _db.SaveChangesAsync(ct);
        }
        return await Project(_db.Reports.AsNoTracking().Where(r => r.Id == reportId)).FirstOrDefaultAsync(ct);
    }

    private static string? Cut(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
