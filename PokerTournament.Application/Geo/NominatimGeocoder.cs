using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PokerTournament.Application.Geo;

/// <summary>
/// Geocodificador do Nominatim/OpenStreetMap (SPEC-20261108-1634, D5): GET /search?q=…&amp;format=jsonv2&amp;countrycodes=br&amp;limit=1
/// com o <c>User-Agent</c> que identifica o app. Singleton: guarda a hora do último pedido e espera o que falta para
/// nunca passar de 1 pedido por segundo (política de uso do serviço), mesmo que mais de um lugar chame — a fila
/// (<see cref="GeocodingQueue"/>) já é sequencial, isto é a segunda trava. Resposta vazia = não achado; falha de rede,
/// tempo esgotado, 429 ou 5xx = <see cref="GeocoderUnavailableException"/> (quem chama tenta depois).
/// </summary>
public class NominatimGeocoder : IGeocoder
{
    public const string HttpClientName = "nominatim";

    private readonly IHttpClientFactory _http;
    private readonly GeoOptions _options;
    private readonly ILogger<NominatimGeocoder> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public NominatimGeocoder(IHttpClientFactory http, IOptions<GeoOptions> options, ILogger<NominatimGeocoder> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "Nominatim";

    public async Task<GeocodeResult> GeocodeAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GeocodeResult.NotFound;

        await _gate.WaitAsync(ct);
        try
        {
            // Nunca mais de um pedido por MinIntervalMs, contado do FIM do pedido anterior (a chegada do próximo no servidor
            // fica sempre a mais de 1 s da chegada do anterior, mesmo com a rede variando)
            var wait = _lastRequestAt + TimeSpan.FromMilliseconds(_options.MinIntervalMs) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);

            var url = $"{_options.NominatimBaseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query)}&format=jsonv2&countrycodes=br&limit=1";
            try
            {
                var client = _http.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Headers.TryAddWithoutValidation("Accept-Language", "pt-BR");
                using var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    // 429 e 5xx são passageiros; 4xx de verdade (consulta ruim) tratamos como "não achado"
                    if (status == (int)HttpStatusCode.TooManyRequests || status >= 500)
                        throw new GeocoderUnavailableException($"Nominatim respondeu {status}.");
                    _logger.LogWarning("Nominatim respondeu {Status} para a consulta {Query}", status, query);
                    return GeocodeResult.NotFound;
                }
                var body = await response.Content.ReadAsStringAsync(ct);
                return Parse(body, query);
            }
            catch (GeocoderUnavailableException)
            {
                throw;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new GeocoderUnavailableException("O Nominatim não respondeu a tempo.");
            }
            catch (HttpRequestException ex)
            {
                throw new GeocoderUnavailableException("Falha de rede ao consultar o Nominatim.", ex);
            }
        }
        finally
        {
            _lastRequestAt = DateTimeOffset.UtcNow;
            _gate.Release();
        }
    }

    private GeocodeResult Parse(string body, string query)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return GeocodeResult.NotFound;
            var first = doc.RootElement[0];
            if (!TryReadDouble(first, "lat", out var lat) || !TryReadDouble(first, "lon", out var lon))
                return GeocodeResult.NotFound;
            var name = first.TryGetProperty("display_name", out var dn) && dn.ValueKind == JsonValueKind.String ? dn.GetString() : null;
            return GeocodeResult.Of(new GeocodePoint(lat, lon, name));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Resposta do Nominatim fora do formato para a consulta {Query}", query);
            throw new GeocoderUnavailableException("Resposta do Nominatim fora do formato.", ex);
        }
    }

    // O jsonv2 manda lat/lon como string ("-23.56"); aceita número também
    private static bool TryReadDouble(JsonElement element, string name, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var prop))
            return false;
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }
}
