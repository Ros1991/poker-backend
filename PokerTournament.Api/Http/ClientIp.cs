using System.Net;

namespace PokerTournament.Api.Http;

/// <summary>
/// IP de quem chamou, para o limitador por IP e para auditoria. Atrás do proxy do Railway
/// o IP real vem no X-Forwarded-For, mas o cliente pode mandar o próprio cabeçalho: o
/// proxy só ACRESCENTA o IP de quem conectou no FIM da lista. Por isso vale o valor mais
/// à direita (o único que o cliente não controla). O valor sai sempre na forma canônica do
/// endereço, sem porta ("1.2.3.4:5678" e "[::1]:80" viram "1.2.3.4" e "::1"): senão a mesma
/// conexão cairia num balde novo do limitador a cada porta.
/// </summary>
public static class ClientIp
{
    public static string Resolve(HttpContext httpContext)
    {
        var lastHop = httpContext.Request.Headers["X-Forwarded-For"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return !string.IsNullOrWhiteSpace(lastHop)
            ? Canonical(lastHop)
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
    }

    /// <summary>Endereço sem porta na forma canônica; texto que não é IP volta como veio.</summary>
    public static string Canonical(string value)
    {
        var text = value.Trim();
        IPAddress? address = null;
        if (IPAddress.TryParse(text, out var parsed))
            address = parsed;
        else if (IPEndPoint.TryParse(text, out var endPoint))
            address = endPoint.Address; // "1.2.3.4:5678" / "[2001:db8::1]:443"
        if (address is null)
            return text;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return address.ToString();
    }
}
