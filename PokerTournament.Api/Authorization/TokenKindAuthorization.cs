using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PokerTournament.Application.Common;

namespace PokerTournament.Api.Authorization;

/// <summary>
/// Libera um endpoint para tokens de escopo restrito (ex.: dealer por QR). Sem este
/// atributo, um token desses é BLOQUEADO em qualquer endpoint [Authorize] — o
/// padrão é negar, então endpoint novo já nasce fechado para o dealer.
/// O valor da rota em <see cref="TournamentRouteKey"/> precisa ser o torneio do token;
/// null = endpoint sem torneio na rota (o próprio endpoint confere o claim).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowTokenKindAttribute : Attribute
{
    public AllowTokenKindAttribute(params string[] kinds)
    {
        Kinds = kinds;
    }

    public string[] Kinds { get; }
    public string? TournamentRouteKey { get; init; } = "tournamentId";
}

/// <summary>Requisito presente em TODAS as políticas (padrão e nomeadas).</summary>
public sealed class TokenKindRequirement : IAuthorizationRequirement;

public sealed class TokenKindHandler : AuthorizationHandler<TokenKindRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TokenKindRequirement requirement)
    {
        var kind = context.User.FindFirst(TokenKinds.ClaimType)?.Value;

        // Token de usuário (inclusive os emitidos antes desta mudança): segue o fluxo de sempre
        if (string.IsNullOrEmpty(kind))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var http = context.Resource switch
        {
            HttpContext h => h,
            HubInvocationContext hub => hub.Context.GetHttpContext(),
            _ => null,
        };
        var allow = http?.GetEndpoint()?.Metadata.GetMetadata<AllowTokenKindAttribute>();
        if (http is null || allow is null || !allow.Kinds.Contains(kind))
            return Task.CompletedTask; // não aprova → 403

        if (allow.TournamentRouteKey is { } routeKey)
        {
            var tokenTournament = context.User.FindFirst(TokenKinds.TournamentClaim)?.Value;
            var routeTournament = http.Request.RouteValues.TryGetValue(routeKey, out var value)
                ? value?.ToString()
                : null;
            if (routeTournament is null
                || !string.Equals(routeTournament, tokenTournament, StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
