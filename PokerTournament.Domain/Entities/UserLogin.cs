namespace PokerTournament.Domain.Entities;

/// <summary>
/// Login externo vinculado a uma conta (SPEC-20261023-1210): provedor + <c>sub</c> do ID token
/// → <see cref="User"/>. A identidade é SEMPRE o <c>sub</c> (fixo e nunca reutilizado pelo
/// provedor), nunca o e-mail; o e-mail do provedor fica só como referência. Provedor + subject
/// são únicos: um login Google pertence a uma conta só.
/// </summary>
public class UserLogin : BaseEntity
{
    public Guid UserId { get; set; }
    /// <summary>nameof(ExternalLoginProvider.X), ex.: "Google".</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Claim <c>sub</c> do ID token do provedor.</summary>
    public string Subject { get; set; } = string.Empty;
    /// <summary>E-mail que o provedor informou na última entrada (minúsculas).</summary>
    public string? Email { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
