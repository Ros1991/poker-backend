using PokerTournament.Domain.Enums;

namespace PokerTournament.Application.Common;

/// <summary>
/// Configuração do login social (SPEC-20261023-1210). Client ID vazio = provedor desligado.
/// Google: <c>Auth:Google:ClientId</c> (default no appsettings.json, sobrescrevível por
/// <c>Auth__Google__ClientId</c>). Apple: <c>Auth:Apple:ClientId</c> (Services ID) — vazio hoje (D17).
/// Nada aqui é segredo: o Client ID é público e o site o lê de <c>GET auth/external/providers</c>.
/// </summary>
public class ExternalLoginOptions
{
    public const string GoogleClientIdKey = "Auth:Google:ClientId";
    public const string AppleClientIdKey = "Auth:Apple:ClientId";

    public string? GoogleClientId { get; set; }
    public string? AppleClientId { get; set; }

    /// <summary>Client ID do provedor, ou null quando ele está desligado.</summary>
    public string? ClientIdFor(ExternalLoginProvider provider)
    {
        var value = provider switch
        {
            ExternalLoginProvider.Google => GoogleClientId,
            ExternalLoginProvider.Apple => AppleClientId,
            _ => null
        };
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

/// <summary>
/// Um provedor OIDC suportado: onde buscar as chaves (discovery), quem pode emitir o token e
/// como falar dele nas mensagens. A Apple fica definida (modelo pronto) e só liga com o Client ID.
/// </summary>
public sealed class ExternalProviderDefinition
{
    public required ExternalLoginProvider Provider { get; init; }
    /// <summary>Segmento da rota e nome no <c>GET auth/external/providers</c> (ex.: "google").</summary>
    public required string Key { get; init; }
    /// <summary>"Google" / "Apple".</summary>
    public required string DisplayName { get; init; }
    /// <summary>"o Google" / "a Apple" (para "entrar com o Google", "conectar a Apple").</summary>
    public required string WithArticle { get; init; }
    public required string DiscoveryUrl { get; init; }
    /// <summary>Valores aceitos no claim <c>iss</c>.</summary>
    public required IReadOnlyList<string> Issuers { get; init; }

    /// <summary>Gravado em <c>user_logins.provider</c> (nameof do enum).</summary>
    public string StoredName => Provider.ToString();

    /// <summary>400 neutro de qualquer falha de token (assinatura, emissor, audience, validade, nonce).</summary>
    public string FailureMessage => $"Não foi possível entrar com {WithArticle}. Tente de novo.";

    public string NoVerifiedEmailMessage => $"Sua conta {DisplayName} não informou um e-mail verificado.";

    /// <summary>Título do aviso de vínculo: "O Google foi conectado à sua conta".</summary>
    public required string ConnectedTitle { get; init; }

    public string LinkRequiresPasswordMessage =>
        $"Já existe uma conta com este e-mail. Entre com sua senha para conectar {WithArticle}.";
}

public static class ExternalLoginProviders
{
    public static readonly ExternalProviderDefinition Google = new()
    {
        Provider = ExternalLoginProvider.Google,
        Key = "google",
        DisplayName = "Google",
        WithArticle = "o Google",
        ConnectedTitle = "O Google foi conectado à sua conta",
        DiscoveryUrl = "https://accounts.google.com/.well-known/openid-configuration",
        Issuers = ["https://accounts.google.com", "accounts.google.com"],
    };

    // Modelo pronto, desligado (D17): só entra em uso com Auth:Apple:ClientId preenchido
    public static readonly ExternalProviderDefinition Apple = new()
    {
        Provider = ExternalLoginProvider.Apple,
        Key = "apple",
        DisplayName = "Apple",
        WithArticle = "a Apple",
        ConnectedTitle = "A Apple foi conectada à sua conta",
        DiscoveryUrl = "https://appleid.apple.com/.well-known/openid-configuration",
        Issuers = ["https://appleid.apple.com"],
    };

    public static readonly IReadOnlyList<ExternalProviderDefinition> All = [Google, Apple];

    /// <summary>Provedor ligado pela chave da rota ("google"), ou null (desconhecido ou desligado).</summary>
    public static ExternalProviderDefinition? FindEnabled(string? key, ExternalLoginOptions options)
    {
        var provider = All.FirstOrDefault(p => string.Equals(p.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
        return provider is not null && options.ClientIdFor(provider.Provider) is not null ? provider : null;
    }

    /// <summary>Provedor pelo nome gravado em <c>user_logins.provider</c>.</summary>
    public static ExternalProviderDefinition? FromStoredName(string? storedName) =>
        All.FirstOrDefault(p => p.StoredName == storedName);
}
