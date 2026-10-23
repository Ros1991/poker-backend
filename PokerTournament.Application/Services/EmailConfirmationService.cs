using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerTournament.Application.Common;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// "Confirme seu e-mail" do cadastro: conta nova nasce com <see cref="User.EmailConfirmedAt"/> nulo
/// (login bloqueado) e recebe um link de uso único válido por 48 h; clicar confirma a conta.
/// Mesmo molde do <see cref="PasswordResetService"/>: só o hash do token fica no banco, o envio
/// roda em segundo plano com escopo de DI próprio e fica registrado em notification_logs
/// (template "email_confirmation", sem token nem link). "Reenviar" responde sempre neutro e tem
/// cooldown de 2 min por conta (o limite por IP fica na política "public-anon" do controller).
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
    /// Serializa, por e-mail normalizado, quem cria/sobrescreve conta ou emite link de confirmação
    /// (cadastro repetido, reenviar): duas requisições simultâneas com o mesmo e-mail viram uma
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

    /// <summary>Já saiu link para esta conta nos últimos 2 min?</summary>
    public Task<bool> IsInCooldownAsync(Guid userId, CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow - Cooldown;
        return _db.EmailConfirmationTokens.AnyAsync(t => t.UserId == userId && t.CreatedAt > since, ct);
    }

    /// <summary>
    /// Gera o token de 48 h (revogando os pendentes e limpando os velhos) e grava, sem enviar nada.
    /// Participa da transação de quem chama, se houver. Devolve o link completo
    /// ({PUBLIC_APP_URL}/confirmar-email/{token}).
    /// </summary>
    public async Task<string> IssueTokenAsync(Guid userId, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cleanupBefore = now - CleanupAge;

        await _db.EmailConfirmationTokens
            .Where(t => t.UserId == userId
                && (t.UsedAt != null || t.RevokedAt != null || t.ExpiresAt <= now)
                && t.CreatedAt < cleanupBefore)
            .ExecuteDeleteAsync(ct);

        await RevokePendingAsync(userId, ct);

        var token = GenerateToken();
        _db.EmailConfirmationTokens.Add(new EmailConfirmationToken
        {
            UserId = userId,
            TokenHash = AuthService.HashToken(token),
            ExpiresAt = now + TokenLifetime,
            RequestedIp = Truncate(ip, 45),
            UserAgent = Truncate(userAgent, 300),
        });
        await _db.SaveChangesAsync(ct);

        return _publicApp.BuildUrl($"{ConfirmPath}/{token}");
    }

    /// <summary>
    /// Revoga os links de confirmação ainda válidos da conta (envio novo, senha redefinida pelo
    /// link do e-mail, login com Google na SPEC-20261023-1210). Devolve quantos foram revogados.
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
    /// Monta o e-mail "Confirme seu e-mail" e dispara o envio em segundo plano. Chamar só DEPOIS
    /// de o token estar gravado (e a transação de quem chama, confirmada).
    /// </summary>
    public void QueueEmail(User user, string link)
    {
        var message = EmailConfirmationEmail.Build(
            user.Email, user.FullName, link, _publicApp.BuildUrl("brand/logo-completa.png"));
        // Fire-and-forget de propósito: a resposta sai agora, com o mesmo tempo de quem não tem
        // conta. O envio usa escopo próprio (o desta requisição morre com ela).
        _ = Task.Run(() => SendInBackgroundAsync(message));
    }

    /// <summary>Emite um link novo (revogando o anterior) e envia em segundo plano.</summary>
    public async Task SendAsync(User user, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var link = await IssueTokenAsync(user.Id, ip, userAgent, ct);
        QueueEmail(user, link);
    }

    /// <summary>
    /// "Reenviar e-mail de confirmação". Nunca lança nem diz nada sobre o e-mail: inexistente,
    /// inativo, já confirmado ou em cooldown (2 min) simplesmente não geram nada.
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
            link = await IssueTokenAsync(user.Id, ip, userAgent, ct);
            await tx.CommitAsync(ct);
        }

        QueueEmail(user, link);
    }

    /// <summary>
    /// Confirma o e-mail a partir do link. Token inexistente, expirado, usado ou revogado (ou conta
    /// inativa) → <see cref="DomainException"/> (400) com <see cref="InvalidLinkMessage"/>. O token é
    /// "consumido" com UPDATE condicional: dois cliques simultâneos no mesmo link dão um 204 e um 400.
    /// Confirmar NÃO loga (D16.8).
    /// </summary>
    public async Task ConfirmAsync(string token, string? ip, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new DomainException(InvalidLinkMessage);

        var now = DateTimeOffset.UtcNow;
        var hash = AuthService.HashToken(token.Trim());

        var row = await _db.EmailConfirmationTokens
            .AsNoTracking()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (row is null || row.UsedAt is not null || row.RevokedAt is not null
            || row.ExpiresAt <= now || !row.User.IsActive)
            throw new DomainException(InvalidLinkMessage);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var claimed = await _db.EmailConfirmationTokens
            .Where(t => t.Id == row.Id && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.UsedAt, now)
                .SetProperty(t => t.UpdatedAt, now), ct);
        if (claimed == 0)
            throw new DomainException(InvalidLinkMessage); // outro clique levou o mesmo link agora

        var user = await _db.Users.FirstAsync(u => u.Id == row.UserId, ct);
        if (user.EmailConfirmedAt is null)
        {
            user.EmailConfirmedAt = now;
            _db.AuditLogs.Add(new AuditLog
            {
                EntityType = "User",
                EntityId = user.Id,
                Action = AuditAction,
                NewValues = """{"via":"link"}""",
                UserId = user.Id,
                UserName = user.FullName,
                IpAddress = Truncate(ip, 50),
                UserAgent = Truncate(userAgent, 500),
                CreatedAt = now
            });
            await _db.SaveChangesAsync(ct);
        }

        // Qualquer outro link da conta deixa de valer
        await RevokePendingAsync(user.Id, ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("E-mail confirmado pelo link para o usuário {UserId}", user.Id);
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
