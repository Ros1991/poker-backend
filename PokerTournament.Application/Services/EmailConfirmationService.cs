using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Services;

/// <summary>Dados de um cadastro presos ao token de confirmação (D16.10): só vão para a conta quando ESTE token é confirmado.</summary>
public sealed record PendingRegistration(string FullName, string? Nickname, string? Whatsapp, string PasswordHash);

/// <summary>
/// "Confirme seu e-mail" do cadastro: conta nova nasce com <see cref="User.EmailConfirmedAt"/> nulo
/// (login bloqueado) e recebe um link de uso único válido por 48 h. D16.10: o link guarda os dados do cadastro que
/// o gerou (<see cref="PendingRegistration"/>) e só confirma com a senha DAQUELE cadastro — a confirmação
/// (<see cref="AuthService.ConfirmEmailAsync"/>) aplica os dados e já devolve a sessão.
/// Mesmo molde do <see cref="PasswordResetService"/>: só o hash do token fica no banco, o envio
/// roda em segundo plano com escopo de DI próprio e fica registrado em notification_logs
/// (template "email_confirmation", sem token nem link). "Reenviar" responde sempre neutro e tem
/// cooldown de 2 min por conta, contado só pelos links que saíram por e-mail (o limite por IP fica na política
/// "public-anon" do controller).
/// </summary>
public class EmailConfirmationService
{
    public const string InvalidLinkMessage = "Link inválido ou expirado. Peça um novo e-mail de confirmação.";
    public const string ConfirmPath = "confirmar-email";
    public const string AuditAction = "EmailConfirmed";

    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(48);
    // Segunda camada de proteção (a primeira é o limite por IP): ninguém bombardeia a caixa de
    // entrada de outra pessoa reenviando o link ou repetindo o cadastro com o e-mail dela.
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);
    // Tokens usados/expirados há mais de 7 dias são apagados no próprio envio (sem job)
    private static readonly TimeSpan CleanupAge = TimeSpan.FromDays(7);
    // Espaço próprio das travas consultivas por e-mail (pg_advisory_xact_lock(int, int))
    private const int EmailLockSpace = 1209;

    private readonly IAppDbContext _db;
    private readonly IServiceScopeFactory _scopes;
    private readonly PublicAppOptions _publicApp;
    private readonly ILogger<EmailConfirmationService> _logger;

    public EmailConfirmationService(
        IAppDbContext db,
        IServiceScopeFactory scopes,
        IOptions<PublicAppOptions> publicApp,
        ILogger<EmailConfirmationService> logger)
    {
        _db = db;
        _scopes = scopes;
        _publicApp = publicApp.Value;
        _logger = logger;
    }

    /// <summary>Mensagem do 403 do login: "… Enviamos um link para ro***@gmail.com."</summary>
    public static string NotConfirmedMessage(string email) =>
        $"Confirme seu e-mail antes de entrar. Enviamos um link para {PasswordResetEmail.Mask(email)}.";

    /// <summary>
    /// Serializa, por e-mail normalizado, quem cria/sobrescreve conta, emite ou confirma link de confirmação
    /// (cadastro repetido, reenviar, confirmar, Google): duas requisições simultâneas com o mesmo e-mail viram uma
    /// fila determinística — independe de o índice único em lower(email) existir no banco (ele não
    /// é criado quando há contas legadas que diferem só por maiúsculas). Tem que rodar DENTRO de uma
    /// transação explícita (a trava solta no commit/rollback).
    /// </summary>
    public static async Task LockEmailAsync(IAppDbContext db, string normalizedEmail, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A trava por e-mail exige uma transação aberta.");

        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({EmailLockSpace}, hashtext({normalizedEmail}))", ct);
    }

    /// <summary>Já saiu link POR E-MAIL para esta conta nos últimos 2 min? (token criado sem e-mail não conta)</summary>
    public Task<bool> IsInCooldownAsync(Guid userId, CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow - Cooldown;
        return _db.EmailConfirmationTokens.AnyAsync(t => t.UserId == userId && t.EmailSent && t.CreatedAt > since, ct);
    }

    /// <summary>
    /// Gera o token de 48 h (revogando os pendentes e limpando os velhos) e grava, sem enviar nada.
    /// <paramref name="pending"/> = dados do cadastro que ficam presos ao token; null (reenviar) = os do cadastro
    /// pendente mais novo da conta. Só o token novo fica com dados: os anteriores perdem os deles.
    /// <paramref name="emailSent"/> = false quando o e-mail não vai sair (cadastro repetido no cooldown).
    /// Participa da transação de quem chama, se houver. Devolve o link completo
    /// ({PUBLIC_APP_URL}/confirmar-email/{token}).
    /// </summary>
    public async Task<string> IssueTokenAsync(
        Guid userId, string? ip, string? userAgent, PendingRegistration? pending = null, bool emailSent = true,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cleanupBefore = now - CleanupAge;

        pending ??= await LatestPendingAsync(userId, ct);

        await _db.EmailConfirmationTokens
            .Where(t => t.UserId == userId
                && (t.UsedAt != null || t.RevokedAt != null || t.ExpiresAt <= now)
                && t.CreatedAt < cleanupBefore)
            .ExecuteDeleteAsync(ct);

        await ClearPendingDataAsync(userId, ct);
        await RevokePendingAsync(userId, ct);

        var token = GenerateToken();
        _db.EmailConfirmationTokens.Add(new EmailConfirmationToken
        {
            UserId = userId,
            TokenHash = AuthService.HashToken(token),
            ExpiresAt = now + TokenLifetime,
            RequestedIp = Truncate(ip, 45),
            UserAgent = Truncate(userAgent, 300),
            EmailSent = emailSent,
            PendingFullName = pending?.FullName,
            PendingNickname = pending?.Nickname,
            PendingWhatsapp = pending?.Whatsapp,
            PendingPasswordHash = pending?.PasswordHash,
        });
        await _db.SaveChangesAsync(ct);

        return _publicApp.BuildUrl($"{ConfirmPath}/{token}");
    }

    /// <summary>
    /// Cadastro pendente mais novo da conta (o do token não usado mais recente que guarda dados), ou null.
    /// Vale também para token já vencido: o "Reenviar" de quem demorou mais de 48 h leva os mesmos dados.
    /// </summary>
    public async Task<PendingRegistration?> LatestPendingAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await _db.EmailConfirmationTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.UsedAt == null && t.PendingPasswordHash != null)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.PendingFullName, t.PendingNickname, t.PendingWhatsapp, t.PendingPasswordHash })
            .FirstOrDefaultAsync(ct);
        return row is null
            ? null
            : new PendingRegistration(row.PendingFullName ?? string.Empty, row.PendingNickname, row.PendingWhatsapp, row.PendingPasswordHash!);
    }

    /// <summary>
    /// Revoga os links de confirmação ainda válidos da conta (envio novo). Devolve quantos foram revogados.
    /// Quando a conta fica confirmada por outro caminho, use <see cref="ClearPendingAsync"/>.
    /// </summary>
    public Task<int> RevokePendingAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return _db.EmailConfirmationTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.UpdatedAt, now), ct);
    }

    /// <summary>
    /// A conta foi confirmada por outro caminho (senha redefinida pelo link do e-mail, login com Google na
    /// SPEC-20261023-1210, confirmação por um link): revoga os links pendentes E apaga os cadastros presos a eles —
    /// nenhum dado de cadastro pendente vai mais para a conta. Devolve quantos links foram revogados.
    /// </summary>
    public async Task<int> ClearPendingAsync(Guid userId, CancellationToken ct = default)
    {
        var revoked = await RevokePendingAsync(userId, ct);
        await ClearPendingDataAsync(userId, ct);
        return revoked;
    }

    private Task<int> ClearPendingDataAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return _db.EmailConfirmationTokens
            .Where(t => t.UserId == userId && (t.PendingPasswordHash != null || t.PendingFullName != null
                || t.PendingNickname != null || t.PendingWhatsapp != null))
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.PendingFullName, (string?)null)
                .SetProperty(t => t.PendingNickname, (string?)null)
                .SetProperty(t => t.PendingWhatsapp, (string?)null)
                .SetProperty(t => t.PendingPasswordHash, (string?)null)
                .SetProperty(t => t.UpdatedAt, now), ct);
    }

    /// <summary>
    /// Token do link que ainda confirma: existe, não usado, não revogado, no prazo, conta ativa e ainda não
    /// confirmada. Sem rastreamento, com a conta. Null = link que não vale (a resposta é a mesma para todos os casos).
    /// </summary>
    public async Task<EmailConfirmationToken?> FindUsableAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        var now = DateTimeOffset.UtcNow;
        var hash = AuthService.HashToken(token.Trim());
        var row = await _db.EmailConfirmationTokens
            .AsNoTracking()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is null || row.UsedAt is not null || row.RevokedAt is not null || row.ExpiresAt <= now
            || !row.User.IsActive || row.User.EmailConfirmedAt is not null)
            return null;
        return row;
    }

    /// <summary>
    /// <c>GET auth/confirm-email/{token}/info</c>: só o e-mail mascarado e se o link vale. Não consome nada.
    /// </summary>
    public async Task<ConfirmEmailInfoResponse> GetInfoAsync(string? token, CancellationToken ct = default)
    {
        var row = await FindUsableAsync(token, ct);
        return row is null
            ? new ConfirmEmailInfoResponse { Valid = false, Email = null }
            : new ConfirmEmailInfoResponse { Valid = true, Email = PasswordResetEmail.Mask(row.User.Email) };
    }

    /// <summary>
    /// "Consome" o token com UPDATE condicional: dois cliques simultâneos no mesmo link → um true e um false.
    /// Tem que rodar na transação de quem confirma (desfeito se a confirmação não for até o fim).
    /// </summary>
    public async Task<bool> TryClaimAsync(Guid tokenId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var claimed = await _db.EmailConfirmationTokens
            .Where(t => t.Id == tokenId && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.UsedAt, now)
                .SetProperty(t => t.UpdatedAt, now), ct);
        return claimed > 0;
    }

    /// <summary>
    /// Monta o e-mail "Confirme seu e-mail" e dispara o envio em segundo plano. Chamar só DEPOIS
    /// de o token estar gravado (e a transação de quem chama, confirmada). Saudação fixa: nada digitado no
    /// cadastro vai para o corpo do e-mail.
    /// </summary>
    public void QueueEmail(string email, string link)
    {
        var message = EmailConfirmationEmail.Build(email, link, _publicApp.BuildUrl("brand/logo-completa.png"));
        // Fire-and-forget de propósito: a resposta sai agora, com o mesmo tempo de quem não tem
        // conta. O envio usa escopo próprio (o desta requisição morre com ela).
        _ = Task.Run(() => SendInBackgroundAsync(message));
    }

    /// <summary>Emite um link novo (revogando o anterior, com o cadastro pendente mais novo) e envia em segundo plano.</summary>
    public async Task SendAsync(User user, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var link = await IssueTokenAsync(user.Id, ip, userAgent, ct: ct);
        QueueEmail(user.Email, link);
    }

    /// <summary>
    /// "Reenviar e-mail de confirmação". Nunca lança nem diz nada sobre o e-mail: inexistente,
    /// inativo, já confirmado ou em cooldown (2 min) simplesmente não geram nada. O link novo leva os dados do
    /// cadastro pendente mais novo (D16.10).
    /// </summary>
    public async Task ResendAsync(string email, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var normalized = UserEmail.Normalize(email);
        if (normalized.Length == 0)
            return;

        User user;
        string link;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            // Mesma fila do cadastro: dois reenvios simultâneos não deixam dois links valendo
            await LockEmailAsync(_db, normalized, ct);

            // Contas antigas que diferem só por maiúsculas: a igual ao digitado, senão a mais antiga
            var found = await UserEmail.MatchingEmail(_db.Users.Where(u => u.IsActive), email).FirstOrDefaultAsync(ct);

            if (found is null || found.EmailConfirmedAt is not null)
            {
                _logger.LogInformation(
                    "Reenvio da confirmação de e-mail ignorado para {Email} ({Motivo})",
                    PasswordResetEmail.Mask(normalized), found is null ? "e-mail não cadastrado ou inativo" : "e-mail já confirmado");
                return;
            }

            if (await IsInCooldownAsync(found.Id, ct))
            {
                _logger.LogInformation(
                    "Reenvio da confirmação de e-mail em cooldown para {Email}", PasswordResetEmail.Mask(found.Email));
                return;
            }

            user = found;
            link = await IssueTokenAsync(user.Id, ip, userAgent, ct: ct);
            await tx.CommitAsync(ct);
        }

        QueueEmail(user.Email, link);
    }

    /// <summary>
    /// Envia e registra em notification_logs fora da requisição, com escopo de DI próprio
    /// (IEmailSender + IAppDbContext novos). Qualquer falha vai para o log — nunca sobe.
    /// </summary>
    private async Task SendInBackgroundAsync(EmailMessage message)
    {
        var masked = PasswordResetEmail.Mask(message.To);
        try
        {
            using var scope = _scopes.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            var result = await sender.SendAsync(message, CancellationToken.None);
            await LogNotificationAsync(db, message.To, result, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no envio em segundo plano do e-mail de confirmação para {Email}", masked);
        }
    }

    // Fora da transação do token e no escopo do envio: falha ao registrar não pode impedir
    // (nem desfazer) nada. Sem provedor (LoggingEmailSender) fica "Failed" com o motivo.
    private async Task LogNotificationAsync(IAppDbContext db, string email, EmailSendResult result, CancellationToken ct)
    {
        try
        {
            db.NotificationLogs.Add(new NotificationLog
            {
                Channel = "Email",
                TemplateKey = EmailConfirmationEmail.TemplateKey,
                Status = result.Sent ? "Sent" : "Failed",
                SentAt = result.Sent ? DateTimeOffset.UtcNow : null,
                ErrorMessage = result.Error,
                Message = PasswordResetEmail.Mask(email), // nunca o link nem o token
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível registrar o e-mail de confirmação em notification_logs");
        }
    }

    private static string GenerateToken()
    {
        // 32 bytes aleatórios em Base64Url (~43 caracteres, sem +, / ou =)
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
