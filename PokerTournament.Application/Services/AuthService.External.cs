using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Login social (SPEC-20261023-1210, pesquisa §10). O ID token do provedor é validado pelo
/// <see cref="OidcTokenValidator"/>; a identidade é SEMPRE provedor + <c>sub</c> (tabela user_logins),
/// nunca o e-mail. Ordem das regras, contra sequestro de conta:
/// <list type="number">
/// <item>login já vinculado → entra (conta desativada → 403);</item>
/// <item>sem e-mail verificado no token → 400;</item>
/// <item>nenhuma conta com o e-mail → cria: confirmada, sem senha, plano Mesa, nome e foto do provedor;</item>
/// <item>conta com o e-mail ainda NÃO confirmada → toma posse (D16.9): confirma, apaga a senha, derruba
/// sessões e links pendentes, vincula e entra;</item>
/// <item>conta confirmada, Google, e-mail <c>@gmail.com</c>, sem Google vinculado → vincula sozinho, entra e
/// avisa por e-mail;</item>
/// <item>conta confirmada nos demais casos → 409 <c>link_requires_password</c> com um ticket; a senha da conta
/// (<c>POST auth/login</c> com <c>linkTicket</c>) conclui o vínculo;</item>
/// <item>conta desativada → 403.</item>
/// </list>
/// A resposta de sucesso é a MESMA do login por senha (sessão por aparelho em auth_sessions).
/// </summary>
public partial class AuthService
{
    public const string ExternalLoginLinkedAction = "ExternalLoginLinked";
    public const string ExternalLoginCreatedAction = "ExternalLoginCreated";
    public const string LinkTicketInvalidMessage =
        "O pedido para conectar sua conta venceu. Clique de novo em \"Continuar com Google\".";

    private const string GmailSuffix = "@gmail.com";

    /// <summary>Provedores ligados e o Client ID público de cada um (o site monta os botões a partir daqui).</summary>
    public ExternalProvidersResponse GetExternalProviders() => new()
    {
        Providers = ExternalLoginProviders.All
            .Select(p => new { p.Key, ClientId = _external.ClientIdFor(p.Provider) })
            .Where(p => p.ClientId is not null)
            .Select(p => new ExternalProviderInfo { Provider = p.Key, ClientId = p.ClientId! })
            .ToList()
    };

    /// <summary>
    /// Nonce de uso único para o próximo clique no botão (10 min; prazo menor só no teste de Development). Assinado e
    /// sem estado no servidor (<see cref="ExternalLoginStore"/>): pedir nonce em massa não ocupa memória nem recusa
    /// ninguém; só o nonce gasto por um ID token válido fica guardado até vencer.
    /// </summary>
    public ExternalNonceResponse IssueExternalNonce(TimeSpan? lifetime = null)
    {
        var issued = _externalStore.IssueNonce(lifetime);
        var seconds = (int)Math.Ceiling((issued.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return new ExternalNonceResponse { Nonce = issued.Nonce, ExpiresIn = Math.Max(seconds, 0) };
    }

    /// <summary>
    /// Entrar pelo provedor (<c>POST auth/external/{provider}</c>). Provedor desligado → 404; token inválido
    /// (assinatura, emissor, audience, validade, nonce ausente/reusado/vencido) → 400 neutro; demais saídas
    /// na tabela de regras do resumo da classe.
    /// </summary>
    public async Task<AuthResponse> ExternalLoginAsync(
        string providerKey, ExternalLoginRequest request, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var provider = ExternalLoginProviders.FindEnabled(providerKey, _external)
            ?? throw new KeyNotFoundException("Provedor de login não disponível.");
        var clientId = _external.ClientIdFor(provider.Provider)!;

        // 409 anterior que o site abandonou: não conclui mais nada, o fluxo recomeça do zero
        _externalStore.RevokeTicket(request.LinkTicket);

        if (string.IsNullOrWhiteSpace(request.IdToken) || string.IsNullOrWhiteSpace(request.Nonce))
            throw new DomainException(provider.FailureMessage);

        var identity = await _oidc.ValidateAsync(provider, clientId, request.IdToken, ct)
            ?? throw new DomainException(provider.FailureMessage);

        // O nonce do token tem que ser o que esta API emitiu ao site e que ainda não foi usado (nem venceu)
        if (identity.Nonce is null || !SameText(identity.Nonce, request.Nonce))
        {
            _logger.LogInformation("Login {Provider} recusado: nonce do token diferente do enviado", provider.DisplayName);
            throw new DomainException(provider.FailureMessage);
        }
        if (!_externalStore.ConsumeNonce(request.Nonce))
        {
            _logger.LogInformation("Login {Provider} recusado: nonce reusado, vencido ou desconhecido", provider.DisplayName);
            throw new DomainException(provider.FailureMessage);
        }

        var email = UserEmail.Normalize(identity.Email);
        var emailUsable = identity.EmailVerified && IsPlausibleEmail(email);
        var now = DateTimeOffset.UtcNow;

        User user;
        AuthSession session;
        string refreshToken;
        var notifyLinked = false;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            // Mesma fila do cadastro e do reenviar: cadastro por senha e Google com o mesmo e-mail ao mesmo
            // tempo não criam duas contas nem disputam a posse
            if (emailUsable)
                await EmailConfirmationService.LockEmailAsync(_db, email, ct);

            var login = await _db.UserLogins
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.Provider == provider.StoredName && l.Subject == identity.Subject, ct);

            if (login is not null)
            {
                // (1) Login já vinculado: entra, se a conta estiver ativa
                if (!login.User.IsActive)
                    throw new AccountDisabledException();
                user = login.User;
                login.LastUsedAt = now;
                if (emailUsable)
                    login.Email = email;
                FillPhoto(user, identity.Picture);
            }
            else
            {
                // (2) Sem e-mail verificado não há como achar nem criar conta
                if (!emailUsable)
                    throw new DomainException(provider.NoVerifiedEmailMessage);

                var existing = await UserEmail.MatchingEmail(_db.Users, email).FirstOrDefaultAsync(ct);
                if (existing is null)
                {
                    // (3) Conta nova: confirmada, sem senha, plano Mesa (default), nome e foto do provedor
                    user = new User
                    {
                        FullName = NameFrom(identity.Name, email),
                        Email = email,
                        PasswordHash = null,
                        PhotoUrl = SafePicture(identity.Picture),
                        Role = "Player",
                        IsActive = true,
                        EmailConfirmedAt = now,
                    };
                    _db.Users.Add(user);
                    await _db.SaveChangesAsync(ct); // gera o Id para o vínculo e a sessão
                    AddLogin(user, provider, identity.Subject, email, now);
                    AddAudit(user, ExternalLoginCreatedAction, Json(new { provider = provider.StoredName }), ip, userAgent, now);
                    _logger.LogInformation("Conta criada pelo {Provider}: usuário {UserId}", provider.DisplayName, user.Id);
                }
                else if (!existing.IsActive)
                {
                    // (7) Conta desativada pela administração
                    throw new AccountDisabledException();
                }
                else if (existing.EmailConfirmedAt is null)
                {
                    // (4) Conta ainda não confirmada: quem controla o e-mail manda (D16.9). Senha e links de
                    // quem a criou deixam de valer; sessões não deveriam existir, mas caem também.
                    user = existing;
                    user.EmailConfirmedAt = now;
                    user.PasswordHash = null;
                    user.RefreshToken = null;
                    user.RefreshTokenExpiresAt = null;
                    // Links de confirmação E os cadastros pendentes presos a eles (D16.10): nenhum vai mais para a conta
                    await _emailConfirmation.ClearPendingAsync(user.Id, ct);
                    await _db.PasswordResetTokens
                        .Where(t => t.UserId == user.Id && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
                        .ExecuteUpdateAsync(set => set
                            .SetProperty(t => t.RevokedAt, now)
                            .SetProperty(t => t.UpdatedAt, now), ct);
                    await RevokeAllSessionsAsync(user.Id, ct);
                    AddLogin(user, provider, identity.Subject, email, now);
                    FillPhoto(user, identity.Picture);
                    AddAudit(user, EmailConfirmationService.AuditAction, Json(new { via = provider.Key }), ip, userAgent, now);
                    AddAudit(user, ExternalLoginLinkedAction, Json(new { provider = provider.StoredName, via = "takeover" }), ip, userAgent, now);
                    _logger.LogInformation("Conta não confirmada assumida pelo {Provider}: usuário {UserId}", provider.DisplayName, user.Id);
                }
                else if (provider == ExternalLoginProviders.Google
                    && email.EndsWith(GmailSuffix, StringComparison.Ordinal)
                    && !await _db.UserLogins.AnyAsync(l => l.UserId == existing.Id && l.Provider == provider.StoredName, ct))
                {
                    // (5) O Google garante o e-mail @gmail.com: vincula sozinho e avisa a conta por e-mail
                    user = existing;
                    AddLogin(user, provider, identity.Subject, email, now);
                    FillPhoto(user, identity.Picture);
                    AddAudit(user, ExternalLoginLinkedAction, Json(new { provider = provider.StoredName, via = "gmail" }), ip, userAgent, now);
                    notifyLinked = true;
                    _logger.LogInformation("{Provider} vinculado sozinho (@gmail.com) ao usuário {UserId}", provider.DisplayName, user.Id);
                }
                else
                {
                    // (6) Demais casos (Workspace, outro domínio, Apple, conta que já tem outro Google): a senha
                    // da conta, uma vez, conclui o vínculo
                    var ticket = _externalStore.IssueTicket(
                            provider.StoredName, identity.Subject, email, SafePicture(identity.Picture), existing.Id)
                        ?? throw new DomainException(provider.FailureMessage);
                    _logger.LogInformation("{Provider} pede a senha para vincular ao usuário {UserId}", provider.DisplayName, existing.Id);
                    throw new ExternalLoginLinkRequiredException(provider.LinkRequiresPasswordMessage, ticket, existing.Email);
                }
            }

            user.LastLoginAt = now;
            (session, refreshToken) = NewSession(user.Id, userAgent);
            _db.AuthSessions.Add(session);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            // Corrida rara (o mesmo login do Google entrando em duas abas ao mesmo tempo, ou um caminho que
            // não passa pela trava por e-mail): nada foi gravado; a pessoa clica de novo e cai na regra (1)
            throw new DomainException(provider.FailureMessage);
        }

        // Depois do commit: o aviso só sai se o vínculo existe de verdade
        if (notifyLinked)
            _externalEmail.QueueLinkedNotice(user, provider);

        return BuildResponse(user, session.Id, refreshToken);
    }

    /// <summary>
    /// Senha certa + ticket do 409: conecta o login do provedor à conta. Ticket inexistente, vencido, esgotado,
    /// de outra conta ou login já conectado a outra conta → 400 (sem sessão).
    /// </summary>
    private async Task LinkFromTicketAsync(User user, string ticket, string? ip, string? userAgent, CancellationToken ct)
    {
        var data = _externalStore.TakeTicket(ticket);
        var provider = ExternalLoginProviders.FromStoredName(data?.Provider);
        if (data is null || data.UserId != user.Id || provider is null || _external.ClientIdFor(provider.Provider) is null)
            throw new DomainException(LinkTicketInvalidMessage);

        var now = DateTimeOffset.UtcNow;
        var current = await _db.UserLogins
            .FirstOrDefaultAsync(l => l.Provider == data.Provider && l.Subject == data.Subject, ct);
        if (current is not null)
        {
            if (current.UserId != user.Id)
                throw new DomainException(LinkTicketInvalidMessage);
            current.LastUsedAt = now; // já estava conectado (outra aba concluiu antes)
            return;
        }

        AddLogin(user, provider, data.Subject, data.ProviderEmail, now);
        FillPhoto(user, data.Picture);
        AddAudit(user, ExternalLoginLinkedAction, Json(new { provider = provider.StoredName, via = "password" }), ip, userAgent, now);
        _logger.LogInformation("{Provider} vinculado com a senha ao usuário {UserId}", provider.DisplayName, user.Id);
    }

    private void AddLogin(User user, ExternalProviderDefinition provider, string subject, string? email, DateTimeOffset now) =>
        _db.UserLogins.Add(new UserLogin
        {
            UserId = user.Id,
            Provider = provider.StoredName,
            Subject = subject,
            Email = string.IsNullOrEmpty(email) ? null : Cut(email, 200),
            LastUsedAt = now,
        });

    private void AddAudit(User user, string action, string newValues, string? ip, string? userAgent, DateTimeOffset now) =>
        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "User",
            EntityId = user.Id,
            Action = action,
            NewValues = newValues,
            UserId = user.Id,
            UserName = user.FullName,
            IpAddress = Cut(ip, 50),
            UserAgent = Cut(userAgent, 500),
            CreatedAt = now
        });

    /// <summary>A foto do provedor só entra quando a conta não tem foto.</summary>
    private static void FillPhoto(User user, string? picture)
    {
        if (string.IsNullOrWhiteSpace(user.PhotoUrl) && SafePicture(picture) is { } url)
            user.PhotoUrl = url;
    }

    /// <summary>Só URL https absoluta que cabe na coluna (500).</summary>
    private static string? SafePicture(string? picture)
    {
        if (string.IsNullOrWhiteSpace(picture) || picture.Length > 500 || picture.Any(char.IsWhiteSpace))
            return null;
        return Uri.TryCreate(picture, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? picture : null;
    }

    /// <summary>Nome do provedor (só conta nova); sem nome, a parte do e-mail antes do @.</summary>
    private static string NameFrom(string? providerName, string email)
    {
        var name = (providerName ?? string.Empty).Trim();
        if (name.Length == 0)
            name = email[..email.IndexOf('@')];
        return Cut(name, 200)!;
    }

    private static bool IsPlausibleEmail(string email)
    {
        var at = email.IndexOf('@');
        return email.Length is > 0 and <= 200 && at > 0 && at == email.LastIndexOf('@') && at < email.Length - 1
            && !email.Any(char.IsWhiteSpace);
    }

    private static bool SameText(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string? Cut(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
