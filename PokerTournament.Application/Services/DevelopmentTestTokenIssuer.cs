using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PokerTournament.Application.Services;

/// <summary>Pedido de token de teste: cada campo nulo cai num padrão "token bom".</summary>
public sealed class TestIdTokenRequest
{
    public string? Sub { get; set; }
    public string? Email { get; set; }
    public bool? EmailVerified { get; set; }
    public string? Name { get; set; }
    public string? Picture { get; set; }
    public string? Nonce { get; set; }
    /// <summary>Audience; null = o Client ID do Google configurado.</summary>
    public string? Audience { get; set; }
    /// <summary>Emissor; null = o emissor de teste (outro valor → o token precisa ser recusado).</summary>
    public string? Issuer { get; set; }
    /// <summary>Segundos até vencer; negativo = já vencido. Padrão 3600.</summary>
    public int? ExpiresInSeconds { get; set; }
    /// <summary>Assina com uma chave que a API não conhece (mesmo kid) → assinatura inválida.</summary>
    public bool SignWithUnknownKey { get; set; }
    /// <summary>Claim hd (domínio do Google Workspace).</summary>
    public string? HostedDomain { get; set; }
}

/// <summary>
/// Emissor de ID tokens de TESTE (SPEC-20261023-1210, pesquisa §9). Existe SÓ em Development: o Program.cs
/// registra este singleton e mapeia <c>POST auth/external/test-token</c> apenas nesse ambiente, e o
/// <see cref="OidcTokenValidator"/> só confia nele quando o recebe do DI. A chave RSA nasce na memória a
/// cada subida da API e nunca sai dela; fora de Development um token deste emissor cai na validação do
/// Google (emissor e assinatura errados) e é recusado com o 400 neutro.
/// </summary>
public sealed class DevelopmentTestTokenIssuer : IDisposable
{
    public const string IssuerValue = "https://emissor-de-teste.pokerdagalera.local";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;

    public DevelopmentTestTokenIssuer()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "teste-local-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant() };
    }

    public string Issuer => IssuerValue;

    public SecurityKey SigningKey => _key;

    public string Mint(TestIdTokenRequest request, string defaultAudience)
    {
        var now = DateTime.UtcNow;
        var expiresIn = request.ExpiresInSeconds ?? 3600;
        var expires = now.AddSeconds(expiresIn);
        // Token vencido: emitido 2 h antes de vencer (nbf/iat < exp, senão o próprio handler recusa criar)
        var issuedAt = expiresIn > 0 ? now : expires.AddHours(-2);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = request.Sub ?? Guid.NewGuid().ToString("N"),
            ["email_verified"] = request.EmailVerified ?? true,
        };
        if (request.Email is not null) claims["email"] = request.Email;
        if (request.Name is not null) claims["name"] = request.Name;
        if (request.Picture is not null) claims["picture"] = request.Picture;
        if (request.Nonce is not null) claims["nonce"] = request.Nonce;
        if (request.HostedDomain is not null) claims["hd"] = request.HostedDomain;

        RSA? unknown = null;
        try
        {
            SecurityKey signingKey = _key;
            if (request.SignWithUnknownKey)
            {
                unknown = RSA.Create(2048);
                signingKey = new RsaSecurityKey(unknown) { KeyId = _key.KeyId };
            }

            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = request.Issuer ?? IssuerValue,
                Audience = request.Audience ?? defaultAudience,
                IssuedAt = issuedAt,
                NotBefore = issuedAt,
                Expires = expires,
                Claims = claims,
                SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
            });
        }
        finally
        {
            unknown?.Dispose();
        }
    }

    public void Dispose() => _rsa.Dispose();
}
