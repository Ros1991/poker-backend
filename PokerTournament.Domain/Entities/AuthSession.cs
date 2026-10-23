namespace PokerTournament.Domain.Entities;

/// <summary>
/// Sessão de login de UM aparelho. Cada login cria a sua; renovar ou sair num
/// aparelho não afeta os outros. O refresh token só existe em hash.
/// </summary>
public class AuthSession : BaseEntity
{
    public Guid UserId { get; set; }
    public string RefreshTokenHash { get; set; } = string.Empty; // SHA-256 (hex)
    public DateTimeOffset ExpiresAt { get; set; }               // deslizante (Jwt:RefreshDays)
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? UserAgent { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
