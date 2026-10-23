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
/// "Esqueci minha senha": pedido → e-mail com link de uso único (1h) → senha nova →
/// todas as sessões do usuário caem. A resposta do pedido é sempre neutra (e sempre rápida:
/// o envio acontece em segundo plano, para o tempo de resposta não denunciar se o e-mail
/// existe); no banco só fica o hash do token.
/// </summary>
public class PasswordResetService
{
    public const string InvalidLinkMessage = "Link inválido ou expirado. Peça um novo link em Esqueci minha senha.";
    public const string ResetPath = "redefinir-senha";

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);
    // Segunda camada de proteção (a primeira é o limite por IP): um atacante com vários
    // IPs não bombardeia a caixa de entrada de uma vítima.
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    // Tokens usados/expirados há mais de 7 dias são apagados no próprio pedido (sem job)
    private static readonly TimeSpan CleanupAge = TimeSpan.FromDays(7);

    private readonly IAppDbContext _db;
    private readonly AuthService _auth;
    private readonly IServiceScopeFactory _scopes;
    private readonly PublicAppOptions _publicApp;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        IAppDbContext db,
        AuthService auth,
        IServiceScopeFactory scopes,
        IOptions<PublicAppOptions> publicApp,
        ILogger<PasswordResetService> logger)
    {
        _db = db;
        _auth = auth;
        _scopes = scopes;
        _publicApp = publicApp.Value;
        _logger = logger;
    }

    /// <summary>
    /// Pedido de redefinição. Nunca lança nem diz se o e-mail existe: usuário inexistente,
    /// inativo ou em cooldown simplesmente não gera nada. O envio do e-mail (até 10 s no
    /// Resend) roda em segundo plano depois do token gravado — a requisição não espera.
    /// </summary>
    public async Task RequestAsync(string email, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var normalized = UserEmail.Normalize(email);
        if (normalized.Length == 0)
            return;

        // Contas antigas que diferem só por maiúsculas: a igual ao digitado, senão a mais antiga
        var user = await UserEmail.MatchingEmail(_db.Users, email).FirstOrDefaultAsync(ct);

        if (user is null || !user.IsActive)
        {
            _logger.LogInformation(
                "Pedido de redefinição de senha ignorado para {Email} ({Motivo})",
                PasswordResetEmail.Mask(normalized), user is null ? "e-mail não cadastrado" : "usuário inativo");
            return;
        }

        var now = DateTimeOffset.UtcNow;

        var recent = await _db.PasswordResetTokens
            .AnyAsync(t => t.UserId == user.Id && t.CreatedAt > now - Cooldown, ct);
        if (recent)
        {
            _logger.LogInformation(
                "Pedido de redefinição de senha em cooldown para {Email}", PasswordResetEmail.Mask(user.Email));
            return;
        }

        var (_, link) = await IssueTokenAsync(user.Id, ip, userAgent, ct);

        var message = PasswordResetEmail.Build(user.Email, user.FullName, link);
        // Fire-and-forget de propósito: a resposta 202 sai agora, com o mesmo tempo de quem
        // não tem conta. O envio usa escopo próprio (o desta requisição morre com ela).
        _ = Task.Run(() => SendInBackgroundAsync(message));
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
            _logger.LogError(ex, "Falha no envio em segundo plano do e-mail de redefinição para {Email}", masked);
        }
    }

    /// <summary>
    /// Gera o token (revogando os pendentes e limpando os velhos) sem enviar nada.
    /// Devolve o token puro e o link completo — ponto de reaproveitamento para
    /// "gerar link e mandar por WhatsApp" na administração.
    /// </summary>
    public async Task<(string Token, string Link)> IssueTokenAsync(
        Guid userId, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cleanupBefore = now - CleanupAge;

        await _db.PasswordResetTokens
            .Where(t => t.UserId == userId
                && (t.UsedAt != null || t.ExpiresAt <= now)
                && t.CreatedAt < cleanupBefore)
            .ExecuteDeleteAsync(ct);

        await _db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.UpdatedAt, now), ct);

        var token = GenerateToken();
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = userId,
            TokenHash = AuthService.HashToken(token),
            ExpiresAt = now + TokenLifetime,
            RequestedIp = Truncate(ip, 45),
            UserAgent = Truncate(userAgent, 300),
        });
        await _db.SaveChangesAsync(ct);

        var link = _publicApp.BuildUrl($"{ResetPath}/{token}");
        return (token, link);
    }

    /// <summary>
    /// Troca a senha a partir do link. Token ruim → <see cref="DomainException"/> (400).
    /// A senha é validada ANTES de consultar o token: a mensagem de senha curta não pode
    /// servir de oráculo de que o token existe.
    /// </summary>
    public async Task ResetAsync(
        string token, string newPassword, string? ip, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
            throw new DomainException("A senha deve ter pelo menos 6 caracteres.");

        if (string.IsNullOrWhiteSpace(token))
            throw new DomainException(InvalidLinkMessage);

        var now = DateTimeOffset.UtcNow;
        var hash = AuthService.HashToken(token.Trim());

        var reset = await _db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (reset is null || reset.UsedAt is not null || reset.RevokedAt is not null
            || reset.ExpiresAt <= now || !reset.User.IsActive)
            throw new DomainException(InvalidLinkMessage);

        var user = reset.User;
        user.PasswordHash = AuthService.HashPassword(newPassword);
        // RevokeAllSessionsAsync só revoga auth_sessions; o refresh legado na coluna do
        // usuário ainda viraria sessão em MigrateLegacyTokenAsync — por isso limpa aqui.
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;
        reset.UsedAt = now;

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "User",
            EntityId = user.Id,
            Action = "PasswordReset",
            UserId = user.Id,
            UserName = user.FullName,
            IpAddress = Truncate(ip, 50),
            UserAgent = Truncate(userAgent, 500),
            CreatedAt = now
        });

        await _db.SaveChangesAsync(ct);
        await _auth.RevokeAllSessionsAsync(user.Id, ct);

        _logger.LogInformation("Senha redefinida pelo link para o usuário {UserId}", user.Id);
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
                TemplateKey = PasswordResetEmail.TemplateKey,
                Status = result.Sent ? "Sent" : "Failed",
                SentAt = result.Sent ? DateTimeOffset.UtcNow : null,
                ErrorMessage = result.Error,
                Message = PasswordResetEmail.Mask(email), // nunca o link nem o token
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível registrar o e-mail de redefinição em notification_logs");
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
