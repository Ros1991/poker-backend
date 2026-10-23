namespace PokerTournament.Domain.Entities;

/// <summary>
/// QR do dealer de um torneio. Quem lê NÃO entra direto: cria um pedido
/// (<see cref="DealerRequest"/>) que o organizador autoriza ou recusa. Vale até o
/// torneio terminar, nunca mais que 24h. Gerar um QR novo substitui este
/// (SupersededAt: o código deixa de valer, quem já foi aprovado continua);
/// revogar (RevokedAt) derruba também todos os dealers aprovados por ele.
/// </summary>
public class DealerAccess : BaseEntity
{
    public Guid TournamentId { get; set; }
    public string Code { get; set; } = string.Empty;   // 128 bits, base64url (guardado para reexibir o QR)
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
