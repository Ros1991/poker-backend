namespace PokerTournament.Domain.Entities;

public class User : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Whatsapp { get; set; }
    public string? PhotoUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
    /// <summary>Quando a pessoa clicou no link "Confirme seu e-mail" (null = login bloqueado). Contas anteriores à confirmação: created_at.</summary>
    public DateTimeOffset? EmailConfirmedAt { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }

    // Navigation
    public ICollection<HomeGame> OwnedHomeGames { get; set; } = [];
    public ICollection<HomeGameMember> HomeGameMemberships { get; set; } = [];
}
