namespace PokerTournament.Application.DTOs.Responses;

/// <summary>QR do dealer ativo de um torneio (só para quem administra o torneio).</summary>
public class DealerAccessResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int PendingCount { get; set; }   // pedidos aguardando autorização neste torneio
    public int ApprovedCount { get; set; }  // dealers conectados (aprovados e no prazo) neste torneio
}

/// <summary>Resposta ao pedido de acesso feito pelo aparelho do dealer.</summary>
public class DealerRequestCreatedResponse
{
    public Guid RequestId { get; set; }
    public string Status { get; set; } = string.Empty;   // DealerRequestStatus
    public string TournamentName { get; set; } = string.Empty;
}

/// <summary>
/// Estado do pedido, consultado pelo aparelho a cada poucos segundos. O token só vem
/// quando o pedido está Approved, no prazo, e a chave do aparelho confere.
/// </summary>
public class DealerRequestStatusResponse
{
    public Guid RequestId { get; set; }
    public string Status { get; set; } = string.Empty;   // Pending | Approved | Rejected | Revoked
    public string Name { get; set; } = string.Empty;
    public Guid TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    // Venceu sem decisão (15 min, vira Rejected) ou a aprovação já passou do prazo
    public bool Expired { get; set; }
    public string? AccessToken { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>Pedido na lista do organizador (pendentes e dealers conectados).</summary>
public class DealerRequestResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>Sessão de dealer em uso (o terminal confere ao abrir).</summary>
public class DealerSessionResponse
{
    public Guid RequestId { get; set; }
    public Guid TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public string DealerName { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
