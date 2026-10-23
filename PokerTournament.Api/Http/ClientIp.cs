namespace PokerTournament.Api.Http;

/// <summary>
/// IP de quem chamou, para o limitador por IP e para auditoria. Atrás do proxy do Railway
/// o IP real vem no X-Forwarded-For, mas o cliente pode mandar o próprio cabeçalho: o
/// proxy só ACRESCENTA o IP de quem conectou no FIM da lista. Por isso vale o valor mais
/// à direita (o único que o cliente não controla).
/// </summary>
public static class ClientIp
{
    public static string Resolve(HttpContext httpContext)
    {
        var lastHop = httpContext.Request.Headers["X-Forwarded-For"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return !string.IsNullOrWhiteSpace(lastHop)
            ? lastHop
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
    }
}
