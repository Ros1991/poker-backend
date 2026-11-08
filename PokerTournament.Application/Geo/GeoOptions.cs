namespace PokerTournament.Application.Geo;

/// <summary>
/// Configuração do CEP e da geocodificação (SPEC-20261108-1634), seção <c>Geo</c> do appsettings (ou variáveis
/// <c>Geo__*</c>). Os padrões são os serviços oficiais; as suítes apontam as duas URLs para um servidor falso local.
/// </summary>
public class GeoOptions
{
    public const string SectionName = "Geo";

    /// <summary>Base do ViaCEP: GET {base}/ws/{cep}/json/.</summary>
    public string ViaCepBaseUrl { get; set; } = "https://viacep.com.br";

    /// <summary>Base do Nominatim: GET {base}/search?q=…&amp;format=jsonv2&amp;countrycodes=br&amp;limit=1.</summary>
    public string NominatimBaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

    /// <summary>Identificação exigida pela política de uso do Nominatim.</summary>
    public string UserAgent { get; set; } = "PokerDaGalera/1.0 (contato@pokerdagalera.com.br)";

    /// <summary>Intervalo mínimo entre dois pedidos ao Nominatim (política: no máximo 1 por segundo).</summary>
    public int MinIntervalMs { get; set; } = 1000;

    /// <summary>Tempo máximo de uma consulta ao ViaCEP.</summary>
    public int CepTimeoutSeconds { get; set; } = 5;

    /// <summary>Tempo máximo de uma consulta ao Nominatim.</summary>
    public int GeocodeTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Espera antes de cada nova tentativa depois de uma falha transitória (rede, 5xx, 429), em segundos, separadas por
    /// vírgula: "30,300" = 3 tentativas no total (a primeira na hora, a segunda 30 s depois, a terceira 5 min depois).
    /// </summary>
    public string RetryDelaysSeconds { get; set; } = "30,300";

    /// <summary>Quanto tempo um CEP achado fica no cache em memória.</summary>
    public int CepCacheHours { get; set; } = 24;

    /// <summary>Atrasos das novas tentativas, já interpretados.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays()
    {
        var list = new List<TimeSpan>();
        foreach (var part in (RetryDelaysSeconds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                list.Add(TimeSpan.FromSeconds(seconds));
        return list;
    }
}
