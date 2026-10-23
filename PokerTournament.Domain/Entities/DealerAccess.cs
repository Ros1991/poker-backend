namespace PokerTournament.Domain.Entities;

/// <summary>
/// QR do dealer de um torneio. Quem lê troca o código por um token que só abre o
/// terminal daquele torneio. Vale até o torneio terminar, nunca mais que 24h;
/// gerar um novo revoga o anterior.
/// </summary>
public class DealerAccess : BaseEntity
{
    public Guid TournamentId { get; set; }
    public string Code { get; set; } = string.Empty;   // 128 bits, base64url (guardado para reexibir o QR)
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? LastExchangeAt { get; set; }
    public int ExchangeCount { get; set; }
}
