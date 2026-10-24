namespace PokerTournament.Application.DTOs.Responses;

/// <summary><c>GET auth/external/providers</c>: só os provedores ligados (Client ID configurado).</summary>
public class ExternalProvidersResponse
{
    public List<ExternalProviderInfo> Providers { get; set; } = [];
}

public class ExternalProviderInfo
{
    /// <summary>"google" (segmento de <c>POST auth/external/{provider}</c>).</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Client ID público que o botão oficial usa.</summary>
    public string ClientId { get; set; } = string.Empty;
}

/// <summary><c>POST auth/external/nonce</c>: nonce de uso único para o próximo clique no botão.</summary>
public class ExternalNonceResponse
{
    public string Nonce { get; set; } = string.Empty;
    /// <summary>Segundos até vencer (600).</summary>
    public int ExpiresIn { get; set; }
}
