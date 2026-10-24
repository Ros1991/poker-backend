using Microsoft.Extensions.Options;
using PokerTournament.Application.Common;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Development;

/// <summary>
/// Emissor de tokens de teste do login social (SPEC-20261023-1210). O Program.cs só chama
/// <see cref="MapDevelopmentTestTokenEndpoints"/> quando o ambiente é Development: em qualquer outro ambiente
/// estas rotas (test-token, test-nonce, test-error) não existem — POST auth/external/test-token cai em
/// auth/external/{provider} → 404 — e o <see cref="DevelopmentTestTokenIssuer"/> nem é registrado no DI.
/// </summary>
public static class DevelopmentTestTokenEndpoints
{
    public sealed class TestNonceRequest
    {
        /// <summary>Validade do nonce em segundos (1–600). Serve para testar o nonce vencido sem esperar 10 min.</summary>
        public int LifetimeSeconds { get; set; } = 600;
    }

    public static void MapDevelopmentTestTokenEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth/external")
            .AllowAnonymous()
            .WithTags("Auth (somente Development)");

        // ID token assinado pela chave local; cada campo do corpo ajusta um claim (ver TestIdTokenRequest)
        group.MapPost("/test-token", (TestIdTokenRequest request, DevelopmentTestTokenIssuer issuer,
            IOptions<ExternalLoginOptions> options) =>
        {
            var audience = options.Value.ClientIdFor(Domain.Enums.ExternalLoginProvider.Google) ?? "sem-client-id";
            return Results.Ok(new { idToken = issuer.Mint(request, audience), issuer = issuer.Issuer });
        });

        // Erro inesperado com mensagem interna: o ErrorHandlingMiddleware tem que responder 500 SEM esse texto
        group.MapPost("/test-error", () =>
        {
            throw new ApplicationException(
                "detalhe interno do teste: Host=localhost;Password=segredo",
                new FormatException("detalhe interno da exceção interna"));
        });

        // Nonce com prazo curto (o de produção vale 10 min)
        group.MapPost("/test-nonce", (TestNonceRequest request, AuthService auth) =>
            Results.Ok(auth.IssueExternalNonce(TimeSpan.FromSeconds(Math.Clamp(request.LifetimeSeconds, 1, 600)))));
    }
}
