using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Login com uma sessão por aparelho (<see cref="AuthSession"/>): logar, renovar ou
/// sair num aparelho não afeta os outros. O refresh token NÃO é rotacionado —
/// a rotação fazia duas abas/aparelhos se derrubarem — e sua validade desliza
/// (90 dias) a cada uso. No banco só fica o hash.
/// </summary>
public partial class AuthService
{
    // Renovar a validade no máximo 1x/h por sessão evita uma escrita a cada refresh
    private static readonly TimeSpan SlideInterval = TimeSpan.FromHours(1);
    // O access token (12h) é conferido contra a sessão a cada requisição; o cache segura
    // o volume e logout/revogação gravam "inativa" nele na hora.
    private static readonly TimeSpan SessionCacheTtl = TimeSpan.FromSeconds(60);

    public const string InvalidCredentialsMessage = "E-mail ou senha inválidos.";
    public const string EmailInUseMessage = "E-mail já está em uso.";
    public const string RegisteredMessage =
        "Enviamos um link para confirmar seu e-mail. Ele vale por 48 horas; confira também a caixa de spam.";
    /// <summary>D16.10: link certo com a senha errada do cadastro (o link continua valendo).</summary>
    public const string WrongConfirmPasswordMessage = "Senha incorreta.";

    private readonly IAppDbContext _db;
    private readonly JwtTokenFactory _tokens;
    private readonly IMemoryCache _cache;
    private readonly EmailConfirmationService _emailConfirmation;
    // Login social (SPEC-20261023-1210) — AuthService.External.cs
    private readonly ExternalLoginStore _externalStore;
    private readonly OidcTokenValidator _oidc;
    private readonly ExternalLoginOptions _external;
    private readonly ExternalLoginEmailService _externalEmail;
    // Limite de senhas erradas no login (revisão da onda 5)
    private readonly LoginAttemptLimiter _loginLimiter;
    // Teto diário de cadastros por IP (revisão da SPEC-20261023-1209)
    private readonly RegistrationLimiter _registrationLimiter;
    // Excluir a conta leva junto o home game principal dela (SPEC-20261029-1723) — AuthService.Profile.cs
    private readonly HomeGameDeletionService _homeGameDeletion;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IAppDbContext db,
        JwtTokenFactory tokens,
        IMemoryCache cache,
        EmailConfirmationService emailConfirmation,
        ExternalLoginStore externalStore,
        OidcTokenValidator oidc,
        IOptions<ExternalLoginOptions> external,
        ExternalLoginEmailService externalEmail,
        LoginAttemptLimiter loginLimiter,
        RegistrationLimiter registrationLimiter,
        HomeGameDeletionService homeGameDeletion,
        ILogger<AuthService> logger)
    {
        _registrationLimiter = registrationLimiter;
        _homeGameDeletion = homeGameDeletion;
        _db = db;
        _tokens = tokens;
        _cache = cache;
        _emailConfirmation = emailConfirmation;
        _externalStore = externalStore;
        _oidc = oidc;
        _external = external.Value;
        _externalEmail = externalEmail;
        _loginLimiter = loginLimiter;
        _logger = logger;
    }

    /// <summary>
    /// Sessão do aparelho no token de usuário. O JwtBearer pode renomear "sid" para
    /// ClaimTypes.Sid ao ler o token, então procura os dois. Null = token sem sessão (legado).
    /// </summary>
    public static Guid? GetSessionId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(TokenKinds.SessionClaim)?.Value
            ?? principal.FindFirst(ClaimTypes.Sid)?.Value;
        return Guid.TryParse(value, out var sid) ? sid : null;
    }

    /// <summary>A sessão do token ainda vale? (existe, do usuário, não revogada, no prazo e usuário ativo)</summary>
    public async Task<bool> IsSessionActiveAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(SessionCacheKey(sessionId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SessionCacheTtl;
            var now = DateTimeOffset.UtcNow;
            return await _db.AuthSessions.AsNoTracking()
                .AnyAsync(s => s.Id == sessionId && s.UserId == userId
                    && s.RevokedAt == null && s.ExpiresAt > now && s.User.IsActive, ct);
        });
    }

    /// <summary>
    /// Derruba todos os aparelhos do usuário (troca de senha, de papel, desativação):
    /// revoga as sessões e marca cada uma como inativa no cache na hora.
    /// </summary>
    public async Task RevokeAllSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = await _db.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (ids.Count == 0)
            return;

        await _db.AuthSessions
            .Where(s => ids.Contains(s.Id))
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.UpdatedAt, now), ct);

        // Depois de gravar: uma requisição concorrente não regrava "ativa" no cache
        foreach (var id in ids)
            MarkSessionInactive(id);
    }

    /// <summary>
    /// Entrar com e-mail e senha. Conta só-social (sem senha) responde o mesmo 400 neutro de senha errada —
    /// nunca 500, nunca "esta conta usa o Google". Com <see cref="LoginRequest.LinkTicket"/> (409 do login social),
    /// a senha certa também conecta o login do provedor à conta (SPEC-20261023-1210). Senha errada demais (por IP +
    /// e-mail, por e-mail ou por IP — <see cref="LoginAttemptLimiter"/>) → 429 ANTES de conferir a senha.
    /// </summary>
    public async Task<AuthResponse> LoginAsync(
        LoginRequest request, string? userAgent = null, string? ip = null, CancellationToken ct = default)
    {
        // Reserva a tentativa (429 se alguma janela estourou); só senha errada fica contada
        var attempt = _loginLimiter.Reserve(ip, request.Email);
        var wrongPassword = false;
        try
        {
            // Sem distinguir maiúsculas. Contas antigas que diferem só por maiúsculas podem
            // coexistir: vale a igual ao digitado, senão a mais antiga (UserEmail.MatchingEmail).
            var user = await UserEmail.MatchingEmail(_db.Users.Where(u => u.IsActive), request.Email)
                .FirstOrDefaultAsync(ct);

            // Sem senha gravada (conta criada pelo Google) cai no MESMO 400 de senha errada
            var passwordOk = user?.PasswordHash is not null && VerifyPassword(request.Password, user.PasswordHash);
            // Conta ainda não confirmada: a senha do cadastro pendente (D16.10 — o cadastro repetido não mexe na
            // conta, os dados ficam no link) também leva ao 403 "confirme seu e-mail"; nunca a uma sessão
            if (!passwordOk && user is { EmailConfirmedAt: null }
                && await _emailConfirmation.LatestPendingAsync(user.Id, ct) is { } pending)
                passwordOk = VerifyPassword(request.Password, pending.PasswordHash);

            if (user is null || !passwordOk)
            {
                wrongPassword = true;
                _externalStore.RegisterFailedAttempt(request.LinkTicket);
                throw new DomainException(InvalidCredentialsMessage);
            }

            // Só DEPOIS da senha certa: e-mail/senha errados continuam 400 neutro e não revelam
            // que a conta existe. Sem confirmação não há sessão (403 email_not_confirmed).
            if (user.EmailConfirmedAt is null)
                throw new EmailNotConfirmedException(EmailConfirmationService.NotConfirmedMessage(user.Email));

            // SEC-02: re-hash transparente de senhas legadas (SHA-256) para PBKDF2 no login
            if (IsLegacyHash(user.PasswordHash))
                user.PasswordHash = HashPassword(request.Password);

            user.LastLoginAt = DateTimeOffset.UtcNow;

            // 409 do login social + senha certa: conecta o provedor (falha → 400, sem sessão)
            if (!string.IsNullOrWhiteSpace(request.LinkTicket))
                await LinkFromTicketAsync(user, request.LinkTicket, ip, userAgent, ct);

            var (session, refreshToken) = NewSession(user.Id, userAgent);
            _db.AuthSessions.Add(session);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (request.LinkTicket is not null && ex.InnerException is DbException { SqlState: "23505" })
            {
                // O mesmo login do provedor foi conectado a outra conta no meio do caminho
                throw new DomainException(LinkTicketInvalidMessage);
            }

            // Entrou: as senhas erradas desta conta neste IP deixam de pesar
            attempt.Succeeded(clearIpAndEmail: true);
            return BuildResponse(user, session.Id, refreshToken);
        }
        finally
        {
            // Senha certa (mesmo com 403/400 do ticket) ou erro inesperado: a tentativa não conta
            if (!wrongPassword)
                attempt.Succeeded();
        }
    }

    /// <summary>
    /// Cadastro público (SPEC-20261023-1209): cria a conta com o e-mail NÃO confirmado, sem sessão
    /// e sem LastLoginAt, e manda o link "Confirme seu e-mail" (48 h); conta confirmada (ou desativada) →
    /// "E-mail já está em uso.". D16.10: os dados do cadastro (nome, apelido, WhatsApp e hash da senha) ficam
    /// presos ao token do link e só vão para a conta quando AQUELE token for confirmado com a senha certa.
    /// Cadastro repetido com o e-mail de uma conta ainda não confirmada NÃO mexe na conta: revoga SEMPRE os links
    /// pendentes e cria um token novo com os dados novos — o cooldown de 2 min só decide se sai e-mail (dentro dele
    /// o token novo existe, mas só chega à caixa pelo "Reenviar" depois do cooldown). Assim nenhum link já enviado
    /// confirma uma senha que a dona da caixa não escolheu. Os casos de sucesso devolvem exatamente a mesma resposta.
    /// Dois cadastros simultâneos com o mesmo e-mail entram em fila pela trava consultiva por e-mail (o último a
    /// chegar fica com o link valendo). Teto de cadastros por IP no dia (<see cref="RegistrationLimiter"/>) → 429.
    /// </summary>
    public async Task<RegisterResponse> RegisterAsync(
        RegisterRequest request, string? ip = null, string? userAgent = null, CancellationToken ct = default)
    {
        var email = UserEmail.Normalize(request.Email);
        var fullName = (request.FullName ?? string.Empty).Trim();
        var nickname = NullIfBlank(request.Nickname);
        var whatsapp = NullIfBlank(request.Whatsapp);
        // Tudo validado ANTES de olhar o banco: nenhuma mensagem de validação denuncia conta existente
        ValidateRegistration(email, fullName, nickname, whatsapp, request.Password);
        // Depois da validação (erro de digitação não gasta o dia), antes do banco: e-mail em uso também conta
        _registrationLimiter.Consume(ip);

        var pending = new PendingRegistration(fullName, nickname, whatsapp, HashPassword(request.Password!));
        string accountEmail;
        string? link = null;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await EmailConfirmationService.LockEmailAsync(_db, email, ct);

            // Sem distinguir maiúsculas: "Fulano@x.com" não cadastra se "fulano@x.com" já existe.
            // Contas antigas que diferem só por maiúsculas são todas confirmadas (migration).
            var matches = await UserEmail.MatchingEmail(_db.Users, email).ToListAsync(ct);
            if (matches.Any(u => u.EmailConfirmedAt is not null || !u.IsActive))
                throw new DomainException(EmailInUseMessage);

            var existing = matches.FirstOrDefault();
            if (existing is null)
            {
                // Conta nova: nasce com os dados deste cadastro (o mesmo que fica preso ao primeiro link)
                var user = new User
                {
                    FullName = fullName,
                    Nickname = nickname,
                    Email = email, // sempre em minúsculas no banco
                    Whatsapp = whatsapp,
                    PasswordHash = pending.PasswordHash,
                    Role = "Player",
                    IsActive = true,
                    EmailConfirmedAt = null // login bloqueado até confirmar pelo link
                };
                _db.Users.Add(user);
                await _db.SaveChangesAsync(ct);
                link = await _emailConfirmation.IssueTokenAsync(user.Id, ip, userAgent, pending, emailSent: true, ct);
                accountEmail = user.Email;
            }
            else
            {
                // D16.10: conta ainda não confirmada — a conta fica como está; os dados novos vão para um link novo
                // (revoga os anteriores). No cooldown o link novo existe, mas o e-mail não sai.
                var sendEmail = !await _emailConfirmation.IsInCooldownAsync(existing.Id, ct);
                var issued = await _emailConfirmation.IssueTokenAsync(existing.Id, ip, userAgent, pending, sendEmail, ct);
                link = sendEmail ? issued : null;
                accountEmail = existing.Email;
            }

            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            // Corrida com um caminho que não passa pela trava (ex.: POST /Users do Admin):
            // o índice único de e-mail barrou a segunda conta
            throw new DomainException(EmailInUseMessage);
        }

        // Depois do commit: o link do e-mail só existe se o token já estiver gravado
        if (link is not null)
            _emailConfirmation.QueueEmail(accountEmail, link);

        return new RegisterResponse { Email = accountEmail, Message = RegisteredMessage };
    }

    /// <summary>
    /// Confirmar o e-mail pelo link (D16.10): token válido + a senha do cadastro que gerou o link (a do token; token
    /// sem cadastro preso, anterior à D16.10, usa a senha da conta). Certo → aplica os dados do cadastro à conta,
    /// confirma (AuditLog EmailConfirmed via link), revoga os outros links e devolve a MESMA sessão do login.
    /// Link inexistente/usado/revogado/vencido/de conta já confirmada → 400 "Link inválido ou expirado…" (não conta
    /// como senha errada). Senha errada → 400 "Senha incorreta." SEM consumir o token, contando no
    /// <see cref="LoginAttemptLimiter"/> (IP + e-mail da conta) — estourou → 429 antes de conferir a senha.
    /// Dois cliques simultâneos com a senha certa: um 200 e um 400.
    /// </summary>
    public async Task<AuthResponse> ConfirmEmailAsync(
        string? token, string? password, string? ip = null, string? userAgent = null, CancellationToken ct = default)
    {
        var row = await _emailConfirmation.FindUsableAsync(token, ct)
            ?? throw new DomainException(EmailConfirmationService.InvalidLinkMessage);

        var attempt = _loginLimiter.Reserve(ip, row.User.Email);
        var wrongPassword = false;
        try
        {
            var expectedHash = row.PendingPasswordHash ?? row.User.PasswordHash;
            if (expectedHash is null || !VerifyPassword(password, expectedHash))
            {
                wrongPassword = true;
                throw new DomainException(WrongConfirmPasswordMessage);
            }

            var now = DateTimeOffset.UtcNow;
            User user;
            AuthSession session;
            string refreshToken;
            await using (var tx = await _db.Database.BeginTransactionAsync(ct))
            {
                // Mesma fila do cadastro/reenviar/Google: um cadastro repetido no meio do caminho revoga este link
                // antes (e o UPDATE condicional abaixo falha) ou depois (e já encontra a conta confirmada)
                await EmailConfirmationService.LockEmailAsync(_db, UserEmail.Normalize(row.User.Email), ct);

                if (!await _emailConfirmation.TryClaimAsync(row.Id, ct))
                    throw new DomainException(EmailConfirmationService.InvalidLinkMessage); // outro clique levou o link

                user = await _db.Users.FirstAsync(u => u.Id == row.UserId, ct);
                if (user.EmailConfirmedAt is not null || !user.IsActive)
                    throw new DomainException(EmailConfirmationService.InvalidLinkMessage);

                if (row.PendingPasswordHash is not null)
                {
                    // Os dados do cadastro DESTE link vão para a conta agora
                    user.FullName = string.IsNullOrWhiteSpace(row.PendingFullName) ? user.FullName : row.PendingFullName;
                    user.Nickname = row.PendingNickname;
                    user.Whatsapp = row.PendingWhatsapp;
                    user.PasswordHash = row.PendingPasswordHash;
                }
                else if (IsLegacyHash(user.PasswordHash))
                {
                    user.PasswordHash = HashPassword(password!);
                }

                user.EmailConfirmedAt = now;
                user.LastLoginAt = now;
                user.RefreshToken = null;
                user.RefreshTokenExpiresAt = null;
                AddAudit(user, EmailConfirmationService.AuditAction, """{"via":"link"}""", ip, userAgent, now);
                // Conta confirmada nasce com o home game principal "Clube de <nome>" (SPEC-20261029-1723)
                await HomeGamePrimary.EnsureForAccountAsync(_db, user, ct);
                // E fica com os jogadores cadastrados com o e-mail dela, em qualquer home game (SPEC-20261029-1725)
                await PlayerLinkService.LinkUserAsync(_db, user, ct);

                // Sessões não deveriam existir em conta não confirmada; se houver, caem (a senha pode ter mudado)
                await RevokeAllSessionsAsync(user.Id, ct);
                (session, refreshToken) = NewSession(user.Id, userAgent);
                _db.AuthSessions.Add(session);
                await _db.SaveChangesAsync(ct);

                // Qualquer outro link da conta deixa de valer e nenhum cadastro pendente sobra
                await _emailConfirmation.ClearPendingAsync(user.Id, ct);
                await tx.CommitAsync(ct);
            }

            // Entrou: as senhas erradas desta conta neste IP deixam de pesar
            attempt.Succeeded(clearIpAndEmail: true);
            _logger.LogInformation("E-mail confirmado pelo link (com a senha do cadastro) para o usuário {UserId}", user.Id);
            return BuildResponse(user, session.Id, refreshToken);
        }
        finally
        {
            // Link/senha certos (mesmo que outro clique tenha levado o link) ou erro inesperado: não conta
            if (!wrongPassword)
                attempt.Succeeded();
        }
    }

    private static void ValidateRegistration(
        string email, string fullName, string? nickname, string? whatsapp, string? password)
    {
        if (email.Length == 0)
            throw new DomainException("Informe o e-mail.");
        var at = email.IndexOf('@');
        if (email.Length > 200 || at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1
            || email.Any(char.IsWhiteSpace))
            throw new DomainException("Informe um e-mail válido.");
        if (fullName.Length == 0)
            throw new DomainException("Informe o nome.");
        if (fullName.Length > 200)
            throw new DomainException("O nome deve ter no máximo 200 caracteres.");
        if (nickname is { Length: > 100 })
            throw new DomainException("O apelido deve ter no máximo 100 caracteres.");
        if (whatsapp is { Length: > 20 })
            throw new DomainException("O WhatsApp deve ter no máximo 20 caracteres.");
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            throw new DomainException("A senha deve ter pelo menos 6 caracteres.");
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Renova o access token. Devolve o MESMO refresh token (sem rotação). Token
    /// inválido, revogado ou expirado → <see cref="UnauthorizedAccessException"/>
    /// (401), que é o único caso em que o cliente deve deslogar.
    /// </summary>
    public async Task<AuthResponse> RefreshTokenAsync(
        string refreshToken, string? userAgent = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedAccessException();

        var now = DateTimeOffset.UtcNow;
        var hash = HashToken(refreshToken);

        var session = await _db.AuthSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct)
            ?? await MigrateLegacyTokenAsync(refreshToken, hash, userAgent, now, ct);

        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now || !session.User.IsActive)
            throw new UnauthorizedAccessException();

        if (session.LastUsedAt is null || now - session.LastUsedAt.Value > SlideInterval)
        {
            // ExecuteUpdate não usa o xmin: refresh simultâneo de duas abas não vira 500
            var newExpiry = now.AddDays(_tokens.RefreshDays);
            await _db.AuthSessions
                .Where(s => s.Id == session.Id)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.ExpiresAt, newExpiry)
                    .SetProperty(s => s.LastUsedAt, now)
                    .SetProperty(s => s.UpdatedAt, now), ct);
        }

        return BuildResponse(session.User, session.Id, refreshToken);
    }

    public async Task<UserResponse?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);

        if (user is null) return null;

        return ToUserResponse(user);
    }

    /// <summary>UserResponse de login/refresh/auth/me, com o plano da conta (SPEC-20261023-1205).</summary>
    private static UserResponse ToUserResponse(User user)
    {
        var response = new UserResponse
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.FullName,
            Role = user.Role,
            PhotoUrl = user.PhotoUrl
        };
        PlanCatalog.FillUserPlan(response, user);
        return response;
    }

    /// <summary>Sai só deste aparelho. Token antigo (sem sessão) limpa a coluna legada.</summary>
    public async Task LogoutAsync(Guid userId, Guid? sessionId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (sessionId is not null)
        {
            await _db.AuthSessions
                .Where(s => s.Id == sessionId.Value && s.UserId == userId && s.RevokedAt == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.RevokedAt, now)
                    .SetProperty(s => s.UpdatedAt, now), ct);
            // O access token deste aparelho para de valer já, não em 12h
            MarkSessionInactive(sessionId.Value);
            return;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _db.SaveChangesAsync(ct);
        }
    }

    // Quem estava logado antes desta mudança tem o refresh token na coluna antiga do
    // usuário. No primeiro refresh ele vira uma sessão com o MESMO token — ninguém cai.
    private async Task<AuthSession?> MigrateLegacyTokenAsync(
        string refreshToken, string hash, string? userAgent, DateTimeOffset now, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.RefreshToken == refreshToken && u.RefreshTokenExpiresAt > now && u.IsActive, ct);
        if (user is null)
            return null;

        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            RefreshTokenHash = hash,
            ExpiresAt = now.AddDays(_tokens.RefreshDays),
            LastUsedAt = now,
            UserAgent = Truncate(userAgent),
        };
        _db.AuthSessions.Add(session);
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;

        try
        {
            await _db.SaveChangesAsync(ct);
            return session;
        }
        catch (DbUpdateException)
        {
            // Outra aba migrou o mesmo token ao mesmo tempo: usa a sessão que ela criou
            _db.AuthSessions.Remove(session);
            return await _db.AuthSessions
                .Include(s => s.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);
        }
    }

    private (AuthSession Session, string RefreshToken) NewSession(Guid userId, string? userAgent)
    {
        var refreshToken = GenerateRefreshToken();
        var now = DateTimeOffset.UtcNow;
        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RefreshTokenHash = HashToken(refreshToken),
            ExpiresAt = now.AddDays(_tokens.RefreshDays),
            LastUsedAt = now,
            UserAgent = Truncate(userAgent),
        };
        return (session, refreshToken);
    }

    private AuthResponse BuildResponse(User user, Guid sessionId, string refreshToken) => new()
    {
        AccessToken = _tokens.CreateUserToken(user, sessionId),
        RefreshToken = refreshToken,
        ExpiresIn = _tokens.AccessTokenMinutes * 60,
        User = ToUserResponse(user)
    };

    private void MarkSessionInactive(Guid sessionId) =>
        _cache.Set(SessionCacheKey(sessionId), false, SessionCacheTtl);

    private static string SessionCacheKey(Guid sessionId) => $"auth-session:{sessionId}";

    public static string HashToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string? Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= 300 ? value : value[..300];

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public static string HashPassword(string password)
    {
        // PBKDF2 (SHA-256) com salt aleatório por usuário. Formato: pbkdf2$iter$salt$hash
        const int iterations = 100_000;
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool IsLegacyHash(string? hash) => hash is not null && !hash.StartsWith("pbkdf2$");

    private static bool VerifyPassword(string? password, string hash)
    {
        // Senha ausente no corpo ("password": null) ou hash corrompido: senha errada, nunca 500
        if (string.IsNullOrEmpty(password))
            return false;

        if (!IsLegacyHash(hash))
        {
            var parts = hash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
                return false;
            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }
            if (salt.Length == 0 || expected.Length == 0)
                return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        // Legado: SHA-256 puro (Base64) — comparação em tempo constante.
        using var sha256 = SHA256.Create();
        var legacy = Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(password)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(legacy), Encoding.UTF8.GetBytes(hash));
    }
}
