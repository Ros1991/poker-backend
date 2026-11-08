using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerTournament.Application.DTOs.Responses;

namespace PokerTournament.Application.Geo;

/// <summary>Como terminou a consulta de um CEP.</summary>
public enum CepLookupStatus
{
    Found,
    NotFound,
    /// <summary>O ViaCEP não respondeu (rede, tempo esgotado, 5xx): a tela deixa preencher à mão.</summary>
    Unavailable,
}

public sealed record CepLookup(CepLookupStatus Status, CepResponse? Address)
{
    public static readonly CepLookup NotFound = new(CepLookupStatus.NotFound, null);
    public static readonly CepLookup Unavailable = new(CepLookupStatus.Unavailable, null);
}

/// <summary>
/// Consulta de CEP no ViaCEP pelo servidor (SPEC-20261108-1634, decisão 1): o navegador nunca chama o ViaCEP (sem
/// CORS, limite ou bloqueio no cliente). Cache em memória por CEP (achado: <see cref="GeoOptions.CepCacheHours"/>;
/// não achado: 1 h), tempo máximo <see cref="GeoOptions.CepTimeoutSeconds"/>. Singleton.
/// </summary>
public class ViaCepClient
{
    public const string HttpClientName = "viacep";
    public const string InvalidMessage = "CEP inválido. Digite os 8 números do CEP.";
    public const string NotFoundMessage = "CEP não encontrado. Preencha o endereço à mão.";
    public const string UnavailableMessage = "Não foi possível consultar o CEP agora. Preencha o endereço à mão.";

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly GeoOptions _options;
    private readonly ILogger<ViaCepClient> _logger;

    public ViaCepClient(IHttpClientFactory http, IMemoryCache cache, IOptions<GeoOptions> options, ILogger<ViaCepClient> logger)
    {
        _http = http;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Só os 8 dígitos do CEP, ou null se o texto não tem exatamente 8 dígitos.</summary>
    public static string? NormalizeCep(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 8)
            return null;
        // Também recusa texto com letras no meio ("12a45-678" tem 7 dígitos; "1234-5678x" passa no filtro de dígitos mas
        // o x não é separador): só dígitos, espaço, ponto e hífen
        return raw.All(c => char.IsAsciiDigit(c) || c is ' ' or '-' or '.') ? digits : null;
    }

    /// <summary>"01310100" → "01310-100".</summary>
    public static string FormatCep(string digits) =>
        digits.Length == 8 ? $"{digits[..5]}-{digits[5..]}" : digits;

    /// <summary><paramref name="cep"/> já normalizado (8 dígitos).</summary>
    public async Task<CepLookup> LookupAsync(string cep, CancellationToken ct = default)
    {
        var cacheKey = "cep:" + cep;
        if (_cache.TryGetValue(cacheKey, out CepLookup? cached) && cached is not null)
            return cached;

        var result = await FetchAsync(cep, ct);
        if (result.Status == CepLookupStatus.Found)
            _cache.Set(cacheKey, result, TimeSpan.FromHours(Math.Max(1, _options.CepCacheHours)));
        else if (result.Status == CepLookupStatus.NotFound)
            _cache.Set(cacheKey, result, TimeSpan.FromHours(1));
        return result;
    }

    private async Task<CepLookup> FetchAsync(string cep, CancellationToken ct)
    {
        var url = $"{_options.ViaCepBaseUrl.TrimEnd('/')}/ws/{cep}/json/";
        try
        {
            var client = _http.CreateClient(HttpClientName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.CepTimeoutSeconds)));
            using var response = await client.GetAsync(url, timeout.Token);
            if (response.StatusCode == HttpStatusCode.BadRequest)
                return CepLookup.NotFound; // o ViaCEP responde 400 para CEP fora de formato
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ViaCEP respondeu {Status} para o CEP {Cep}", (int)response.StatusCode, cep);
                return CepLookup.Unavailable;
            }
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return Parse(body, cep);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("ViaCEP não respondeu a tempo para o CEP {Cep}", cep);
            return CepLookup.Unavailable;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Falha de rede ao consultar o ViaCEP para o CEP {Cep}", cep);
            return CepLookup.Unavailable;
        }
    }

    private CepLookup Parse(string body, string cep)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return CepLookup.Unavailable;
            // Não achado: { "erro": true } (ou "erro": "true")
            if (root.TryGetProperty("erro", out var erro) && (erro.ValueKind == JsonValueKind.True || (erro.ValueKind == JsonValueKind.String && erro.GetString() == "true")))
                return CepLookup.NotFound;
            var address = new CepResponse
            {
                Cep = cep,
                Street = Text(root, "logradouro"),
                Complement = Text(root, "complemento"),
                District = Text(root, "bairro"),
                City = Text(root, "localidade"),
                State = Text(root, "uf")?.ToUpperInvariant(),
            };
            if (string.IsNullOrWhiteSpace(address.City) || string.IsNullOrWhiteSpace(address.State))
                return CepLookup.NotFound;
            return new CepLookup(CepLookupStatus.Found, address);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Resposta do ViaCEP fora do formato para o CEP {Cep}", cep);
            return CepLookup.Unavailable;
        }
    }

    private static string? Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
            return null;
        var value = prop.GetString()?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
