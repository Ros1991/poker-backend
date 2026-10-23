namespace PokerTournament.Domain.Entities;

/// <summary>
/// Link "Confirme seu e-mail" enviado no cadastro: uso único, válido por 48 horas.
/// Mesmo formato do <see cref="PasswordResetToken"/> — só o hash SHA-256 do token fica no banco.
/// </summary>
public class EmailConfirmationToken : BaseEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty; // SHA-256 (hex)
    public DateTimeOffset ExpiresAt { get; set; }         // criação + 48h
    public DateTimeOffset? UsedAt { get; set; }           // uso único
    public DateTimeOffset? RevokedAt { get; set; }        // substituído por um envio mais novo (ou conta já confirmada)
    public string? RequestedIp { get; set; }
    public string? UserAgent { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
