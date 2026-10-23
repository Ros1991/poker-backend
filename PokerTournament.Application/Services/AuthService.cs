using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Login com uma sessão por aparelho (<see cref="AuthSession"/>): logar, renovar ou
/// sair num aparelho não afeta os outros. O refresh token NÃO é rotacionado —
/// a rotação fazia duas abas/aparelhos se derrubarem — e sua validade desliza
/// (90 dias) a cada uso. No banco só fica o hash.
/// </summary>
public class AuthService
{
    // Renovar a validade no máximo 1x/h por sessão evita uma escrita a cada refresh
    private static readonly TimeSpan SlideInterval = TimeSpan.FromHours(1);
    // O access token (12h) é conferido contra a sessão a cada requisição; o cache segura
    // o volume e logout/revogação gravam "inativa" nele na hora.
    private static readonly TimeSpan SessionCacheTtl = TimeSpan.FromSeconds(60);

    private readonly IAppDbContext _db;
    private readonly JwtTokenFactory _tokens;
    private readonly IMemoryCache _cache;

    public AuthService(IAppDbContext db, JwtTokenFactory tokens, IMemoryCache cache)
    {
        _db = db;
        _tokens = tokens;
        _cache = cache;
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

    public async Task<AuthResponse> LoginAsync(
        LoginRequest request, string? userAgent = null, CancellationToken ct = default)
    {
        // Sem distinguir maiúsculas. Contas antigas que diferem só por maiúsculas podem
        // coexistir: vale a igual ao digitado, senão a mais antiga (UserEmail.MatchingEmail).
        var user = await UserEmail.MatchingEmail(_db.Users.Where(u => u.IsActive), request.Email)
            .FirstOrDefaultAsync(ct);

        if (user is null)
            throw new DomainException("E-mail ou senha inválidos.");

        if (!VerifyPassword(request.Password, user.PasswordHash))
            throw new DomainException("E-mail ou senha inválidos.");

        // SEC-02: re-hash transparente de senhas legadas (SHA-256) para PBKDF2 no login
        if (IsLegacyHash(user.PasswordHash))
            user.PasswordHash = HashPassword(request.Password);

        user.LastLoginAt = DateTimeOffset.UtcNow;

        var (session, refreshToken) = NewSession(user.Id, userAgent);
        _db.AuthSessions.Add(session);

        await _db.SaveChangesAsync(ct);

        return BuildResponse(user, session.Id, refreshToken);
    }

    public async Task<AuthResponse> RegisterAsync(
        RegisterRequest request, string? userAgent = null, CancellationToken ct = default)
    {
        var email = UserEmail.Normalize(request.Email);
        if (email.Length == 0)
            throw new DomainException("Informe o e-mail.");

        // Sem distinguir maiúsculas: "Fulano@x.com" não cadastra se "fulano@x.com" já existe
        if (await UserEmail.IsTakenAsync(_db.Users, email, null, ct))
            throw new DomainException("E-mail já está em uso.");

        var user = new User
        {
            Id = Guid.NewGuid(), // a sessão precisa do id antes do SaveChanges
            FullName = request.FullName,
            Nickname = request.Nickname,
            Email = email, // sempre em minúsculas no banco
            Whatsapp = request.Whatsapp,
            PasswordHash = HashPassword(request.Password),
            Role = "Player",
            IsActive = true,
            LastLoginAt = DateTimeOffset.UtcNow
        };

        _db.Users.Add(user);

        var (session, refreshToken) = NewSession(user.Id, userAgent);
        _db.AuthSessions.Add(session);

        await _db.SaveChangesAsync(ct);

        return BuildResponse(user, session.Id, refreshToken);
    }

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

        return new UserResponse
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.FullName,
            Role = user.Role,
            PhotoUrl = user.PhotoUrl
        };
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
        User = new UserResponse
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.FullName,
            Role = user.Role,
            PhotoUrl = user.PhotoUrl
        }
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

    private static bool IsLegacyHash(string hash) => !hash.StartsWith("pbkdf2$");

    private static bool VerifyPassword(string password, string hash)
    {
        if (!IsLegacyHash(hash))
        {
            var parts = hash.Split('$');
            if (parts.Length != 4) return false;
            var iterations = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
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
