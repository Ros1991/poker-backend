using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PokerTournament.Application.Common;

namespace PokerTournament.Application.Services;

/// <summary>Quem o ID token diz que é (só depois de assinatura, emissor, audience e validade conferidos).</summary>
public sealed record ExternalIdentity(
    string Subject,
    string? Email,
    bool EmailVerified,
    string? Name,
    string? Picture,
    string? Nonce,
    string? HostedDomain);

/// <summary>
/// Validador OIDC único para Google e Apple (SPEC-20261023-1210), sem pacote novo: usa o
/// <c>JsonWebTokenHandler</c>/<c>JsonWebKeySet</c> do Microsoft.IdentityModel que o JwtBearer 9 já traz.
/// Confere a assinatura pelas chaves publicadas pelo provedor (discovery → jwks_uri, em cache de 6 h e
/// recarregadas quando aparece um <c>kid</c> desconhecido), o emissor, a audience (= Client ID), a
/// validade e os algoritmos que a própria discovery declara (nada fixado: a Apple declara RS256 embora
/// um texto da doc fale em ES256). Nonce e e-mail verificado ficam com quem chama.
/// Em Development, e só lá, aceita também o emissor de tokens de teste (<see cref="DevelopmentTestTokenIssuer"/>,
/// registrado no DI apenas nesse ambiente) para o roteiro de verificação rodar sem o Google de verdade.
/// Qualquer falha devolve null (o motivo vai para o log) — a resposta ao cliente é sempre o 400 neutro.
/// </summary>
public class OidcTokenValidator
{
    public const string HttpClientName = "oidc";

    private static readonly TimeSpan KeysLifetime = TimeSpan.FromHours(6);
    // Recarga forçada (kid desconhecido) no máximo a cada 5 min: token forjado com kid inventado não vira DoS no provedor
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);
    private const int MaxTokenLength = 16 * 1024;

    private readonly IHttpClientFactory _http;
    private readonly DevelopmentTestTokenIssuer? _testIssuer;
    private readonly ILogger<OidcTokenValidator> _logger;
    private readonly ConcurrentDictionary<string, ProviderKeys> _keys = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public OidcTokenValidator(
        IHttpClientFactory http, DevelopmentTestTokenIssuer? testIssuer, ILogger<OidcTokenValidator> logger)
    {
        _http = http;
        _testIssuer = testIssuer;
        _logger = logger;
    }

    public async Task<ExternalIdentity?> ValidateAsync(
        ExternalProviderDefinition provider, string clientId, string? idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > MaxTokenLength)
            return Reject(provider, "token vazio ou grande demais");

        var handler = new JsonWebTokenHandler();
        if (!handler.CanReadToken(idToken))
            return Reject(provider, "token ilegível");

        string? unverifiedIssuer;
        try
        {
            unverifiedIssuer = handler.ReadJsonWebToken(idToken).Issuer;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException)
        {
            return Reject(provider, "token malformado");
        }

        TokenValidationResult result;
        // O emissor ainda não conferido só ESCOLHE o conjunto de chaves; a validação abaixo exige
        // assinatura dessas chaves E emissor igual. Fora de Development _testIssuer é sempre null.
        if (_testIssuer is not null && unverifiedIssuer == _testIssuer.Issuer)
        {
            result = await handler.ValidateTokenAsync(idToken, Parameters(
                clientId, [_testIssuer.Issuer], [_testIssuer.SigningKey], [SecurityAlgorithms.RsaSha256]));
        }
        else
        {
            var keys = await GetKeysAsync(provider, forceRefresh: false, ct);
            if (keys is null)
                return Reject(provider, "chaves do provedor indisponíveis");

            result = await handler.ValidateTokenAsync(idToken, Parameters(clientId, provider.Issuers, keys.Keys, keys.Algorithms));
            if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                // Provedor trocou as chaves: recarrega uma vez e tenta de novo
                var refreshed = await GetKeysAsync(provider, forceRefresh: true, ct);
                if (refreshed is not null && !ReferenceEquals(refreshed, keys))
                    result = await handler.ValidateTokenAsync(idToken, Parameters(clientId, provider.Issuers, refreshed.Keys, refreshed.Algorithms));
            }
        }

        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
            return Reject(provider, result.Exception?.GetType().Name ?? "inválido");

        var subject = Claim(jwt, JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255)
            return Reject(provider, "sem sub");

        return new ExternalIdentity(
            Subject: subject,
            Email: Claim(jwt, "email"),
            // Google manda booleano; a Apple, às vezes, a string "true"
            EmailVerified: string.Equals(Claim(jwt, "email_verified"), "true", StringComparison.OrdinalIgnoreCase),
            Name: Claim(jwt, "name"),
            Picture: Claim(jwt, "picture"),
            Nonce: Claim(jwt, "nonce"),
            HostedDomain: Claim(jwt, "hd"));
    }

    private static TokenValidationParameters Parameters(
        string clientId, IEnumerable<string> issuers, IEnumerable<SecurityKey> keys, IEnumerable<string>? algorithms) => new()
    {
        ValidateIssuer = true,
        ValidIssuers = issuers,
        ValidateAudience = true,
        ValidAudience = clientId,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true,
        IssuerSigningKeys = keys,
        ValidAlgorithms = algorithms,
        ClockSkew = ClockSkew,
        // Os claims do provedor são lidos crus (sub, email…), sem o mapeamento para ClaimTypes.*
        NameClaimType = JwtRegisteredClaimNames.Sub,
        RoleClaimType = ClaimTypes.Role,
    };

    private static string? Claim(JsonWebToken jwt, string type) =>
        jwt.TryGetClaim(type, out var claim) && !string.IsNullOrWhiteSpace(claim.Value) ? claim.Value.Trim() : null;

    private ExternalIdentity? Reject(ExternalProviderDefinition provider, string reason)
    {
        _logger.LogInformation("ID token do {Provider} recusado: {Motivo}", provider.DisplayName, reason);
        return null;
    }

    // ---- Chaves publicadas pelo provedor (discovery → jwks_uri) ----

    private sealed record ProviderKeys(IReadOnlyList<SecurityKey> Keys, IReadOnlyList<string>? Algorithms, DateTimeOffset FetchedAt);

    private async Task<ProviderKeys?> GetKeysAsync(ExternalProviderDefinition provider, bool forceRefresh, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        _keys.TryGetValue(provider.Key, out var cached);
        if (cached is not null && !forceRefresh && now - cached.FetchedAt < KeysLifetime)
            return cached;
        if (cached is not null && forceRefresh && now - cached.FetchedAt < MinRefreshInterval)
            return cached;

        var gate = _locks.GetOrAdd(provider.Key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Outra requisição pode ter recarregado enquanto esperávamos
            if (_keys.TryGetValue(provider.Key, out var current) && !ReferenceEquals(current, cached))
                return current;

            var fresh = await FetchKeysAsync(provider, ct);
            if (fresh is null)
                return cached; // provedor fora do ar: segue com as chaves que já tinha (se tinha)
            _keys[provider.Key] = fresh;
            return fresh;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ProviderKeys?> FetchKeysAsync(ExternalProviderDefinition provider, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(HttpClientName);
            using var discovery = JsonDocument.Parse(await client.GetStringAsync(provider.DiscoveryUrl, ct));
            var jwksUri = discovery.RootElement.TryGetProperty("jwks_uri", out var uri) ? uri.GetString() : null;
            if (string.IsNullOrWhiteSpace(jwksUri) || !jwksUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Discovery do {Provider} sem jwks_uri https", provider.DisplayName);
                return null;
            }

            List<string>? algorithms = null;
            if (discovery.RootElement.TryGetProperty("id_token_signing_alg_values_supported", out var algs)
                && algs.ValueKind == JsonValueKind.Array)
            {
                algorithms = algs.EnumerateArray()
                    .Select(a => a.GetString())
                    .Where(a => !string.IsNullOrWhiteSpace(a) && !string.Equals(a, "none", StringComparison.OrdinalIgnoreCase))
                    .Select(a => a!)
                    .ToList();
                if (algorithms.Count == 0)
                    algorithms = null;
            }

            var set = new JsonWebKeySet(await client.GetStringAsync(jwksUri, ct));
            var keys = set.GetSigningKeys();
            if (keys.Count == 0)
            {
                _logger.LogError("JWKS do {Provider} sem chaves de assinatura", provider.DisplayName);
                return null;
            }

            return new ProviderKeys(keys.ToList(), algorithms, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Não foi possível baixar as chaves do {Provider}", provider.DisplayName);
            return null;
        }
    }
}
