namespace PokerTournament.Application.Geo;

/// <summary>Um ponto achado pelo geocodificador para um texto de consulta.</summary>
public sealed record GeocodePoint(double Latitude, double Longitude, string? ResolvedName);

/// <summary>
/// Resposta de uma consulta: <see cref="Found"/> com o ponto, ou não achado (resposta válida e vazia). Falha de
/// rede/servidor NÃO vira resultado — o geocodificador lança <see cref="GeocoderUnavailableException"/>, para quem
/// chama tentar de novo mais tarde.
/// </summary>
public sealed record GeocodeResult(bool Found, GeocodePoint? Point)
{
    public static readonly GeocodeResult NotFound = new(false, null);
    public static GeocodeResult Of(GeocodePoint point) => new(true, point);
}

/// <summary>O provedor não respondeu (rede, tempo esgotado, 5xx, 429): falha transitória, tentar de novo depois.</summary>
public class GeocoderUnavailableException : Exception
{
    public GeocoderUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Geocodificador (SPEC-20261108-1634): texto de endereço → coordenadas. Hoje o Nominatim
/// (<see cref="NominatimGeocoder"/>); a interface existe para trocar por Google depois sem mexer na fila, no cache nem
/// nas telas. Quem implementa cuida do limite do provedor (o Nominatim aceita 1 pedido por segundo).
/// </summary>
public interface IGeocoder
{
    /// <summary>Nome do provedor gravado no cache (ex.: "Nominatim").</summary>
    string ProviderName { get; }

    /// <summary>Consulta um texto livre de endereço no Brasil.</summary>
    Task<GeocodeResult> GeocodeAsync(string query, CancellationToken ct = default);
}
