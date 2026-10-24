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
        ILogger<AuthService> logger)
    {
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
            if (user?.PasswordHash is null || !VerifyPassword(request.Password, user.PasswordHash))
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
    /// e sem LastLoginAt, e manda o link "Confirme seu e-mail" (48 h). Cadastro repetido com o
    /// e-mail de uma conta ainda não confirmada sobrescreve nome/apelido/WhatsApp/senha e reenvia
    /// o link (D16.4 — quem controla a caixa é quem confirma); conta confirmada (ou desativada) →
    /// "E-mail já está em uso.". Os dois casos de sucesso devolvem exatamente a mesma resposta.
    /// Dois cadastros simultâneos com o mesmo e-mail entram em fila pela trava consultiva por
    /// e-mail: o segundo encontra a conta do primeiro e a sobrescreve (último a chegar vence).
    /// O reenvio do cadastro repetido respeita o cooldown de 2 min do "Reenviar" (o link anterior
    /// continua valendo e confirma a conta com os dados novos).
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

        User user;
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
                user = new User
                {
                    FullName = fullName,
                    Nickname = nickname,
                    Email = email, // sempre em minúsculas no banco
                    Whatsapp = whatsapp,
                    PasswordHash = HashPassword(request.Password),
                    Role = "Player",
                    IsActive = true,
                    EmailConfirmedAt = null // login bloqueado até clicar no link
                };
                _db.Users.Add(user);
            }
            else
            {
                // D16.4: conta ainda não confirmada — os dados novos substituem os antigos
                user = existing;
                user.FullName = fullName;
                user.Nickname = nickname;
                user.Whatsapp = whatsapp;
                user.PasswordHash = HashPassword(request.Password);
            }

            await _db.SaveChangesAsync(ct);

            if (existing is null || !await _emailConfirmation.IsInCooldownAsync(user.Id, ct))
                link = await _emailConfirmation.IssueTokenAsync(user.Id, ip, userAgent, ct);

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
            _emailConfirmation.QueueEmail(user, link);

        return new RegisterResponse { Email = user.Email, Message = RegisteredMessage };
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
