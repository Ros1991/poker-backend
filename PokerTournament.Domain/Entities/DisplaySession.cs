namespace PokerTournament.Domain.Entities;

/// <summary>
/// Uma TV (navegador do telão) sem login. A TV guarda um token secreto (aqui só o
/// hash) e mostra um código curto em QR; quem está logado no celular "reivindica"
/// o código escolhendo o torneio, e a TV passa a exibir o telão dele.
/// </summary>
public class DisplaySession : BaseEntity
{
    public string DeviceTokenHash { get; set; } = string.Empty;   // SHA-256 (hex)
    public string? PairingCode { get; set; }                      // 6 caracteres, sem 0/O/1/I/L
    public DateTimeOffset? CodeExpiresAt { get; set; }
    // Código anterior à última renovação: quem leu o QR pouco antes ainda consegue parear
    public string? PreviousPairingCode { get; set; }
    public DateTimeOffset? PreviousCodeExpiresAt { get; set; }
    public string Status { get; set; } = string.Empty;            // DisplaySessionStatus
    public Guid? TournamentId { get; set; }
    public Guid? PairedByUserId { get; set; }
    public DateTimeOffset? PairedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string? UserAgent { get; set; }
}
