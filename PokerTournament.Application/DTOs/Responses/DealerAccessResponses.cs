namespace PokerTournament.Application.DTOs.Responses;

/// <summary>QR do dealer ativo de um torneio (só para quem administra o torneio).</summary>
public class DealerAccessResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int ExchangeCount { get; set; }
}

/// <summary>Resultado da leitura do QR no celular do dealer.</summary>
public class DealerExchangeResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
}

/// <summary>Sessão de dealer em uso (o terminal confere ao abrir).</summary>
public class DealerSessionResponse
{
    public Guid TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
