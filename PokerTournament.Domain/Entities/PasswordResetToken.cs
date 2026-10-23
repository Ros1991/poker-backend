namespace PokerTournament.Domain.Entities;

/// <summary>
/// Pedido de "Esqueci minha senha": link de uso único válido por 1 hora.
/// Só o hash SHA-256 do token fica no banco (mesma regra do refresh token).
/// </summary>
public class PasswordResetToken : BaseEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty; // SHA-256 (hex)
    public DateTimeOffset ExpiresAt { get; set; }         // criação + 1h
    public DateTimeOffset? UsedAt { get; set; }           // uso único
    public DateTimeOffset? RevokedAt { get; set; }        // substituído por um pedido mais novo
    public string? RequestedIp { get; set; }
    public string? UserAgent { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
