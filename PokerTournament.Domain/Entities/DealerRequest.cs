namespace PokerTournament.Domain.Entities;

/// <summary>
/// Pedido de acesso de um dealer. Ao ler o QR, o aparelho informa o nome e fica
/// esperando o organizador autorizar. A aprovação vale para ESTE pedido (é o sub do
/// token) e só o aparelho que o criou (DeviceKeyHash) recebe o token; revogar o
/// pedido derruba só aquele aparelho.
/// </summary>
public class DealerRequest : BaseEntity
{
    public Guid TournamentId { get; set; }
    public Guid DealerAccessId { get; set; }                   // QR lido no pedido
    public string Name { get; set; } = string.Empty;           // 2–40 caracteres, informado pelo dealer
    public string DeviceKeyHash { get; set; } = string.Empty;  // SHA-256 (hex) da chave que o aparelho gera e guarda
    public string Status { get; set; } = string.Empty;         // DealerRequestStatus
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedBy { get; set; }                       // usuário que decidiu; null quando venceu sozinho
    public DateTimeOffset? ExpiresAt { get; set; }             // definido na aprovação: min(QR.ExpiresAt, agora + 24h)
    public DateTimeOffset? LastSeenAt { get; set; }            // última requisição válida do terminal
    public string? UserAgent { get; set; }
}
