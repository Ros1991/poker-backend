using System.Globalization;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Geo;

/// <summary>Home game com a distância (em km) até o ponto da busca.</summary>
public sealed class HomeGameDistance
{
    public HomeGame HomeGame { get; set; } = null!;
    public double DistanceKm { get; set; }
}

/// <summary>
/// Distância e busca por raio (SPEC-20261108-1634), para a lista "perto de mim" da SPEC-20261108-1639. Fórmula de
/// Haversine traduzida para SQL pelo EF (sin/cos/asin/sqrt do PostgreSQL, sem PostGIS), com uma caixa de
/// latitude/longitude antes para o índice <c>ix_home_games_lat_lng</c> cortar o grosso. Raio padrão 30 km (plano §6.1).
/// </summary>
public static class GeoDistance
{
    /// <summary>Raio médio da Terra em km.</summary>
    public const double EarthRadiusKm = 6371.0088;

    /// <summary>Raio padrão da busca "perto de mim".</summary>
    public const double DefaultRadiusKm = 30;

    /// <summary>
    /// Passo da grade do PONTO PÚBLICO do clube (graus): ~2,2 km de latitude e ~2 km de longitude no Brasil. A página
    /// pública e o "perto de mim" medem a distância até esse ponto, nunca até a coordenada exata (revisão da
    /// SPEC-20261108-1639: com a distância fina, cinco consultas de pontos diferentes trilateravam a casa).
    /// </summary>
    public const double PublicPointStepDegrees = 0.02;

    /// <summary>
    /// Quanto o ponto público pode se afastar do exato (meia diagonal da célula ≈ 1,6 km), com folga: a busca por raio
    /// no banco usa o raio + esta margem e o filtro final, em memória, é pela distância ao ponto público.
    /// </summary>
    public const double PublicPointMarginKm = 2;

    /// <summary>Nome do cabeçalho com a localização do visitante ("lat,lng", ponto decimal), nos dois endpoints.</summary>
    public const string VisitorLocationHeader = "X-Visitor-Location";

    /// <summary>Distância em km entre dois pontos (Haversine), em memória.</summary>
    public static double Kilometers(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>
    /// Home games com coordenadas dentro de <paramref name="radiusKm"/> do ponto, do mais perto ao mais longe, com a
    /// distância calculada no banco. Compõe com qualquer filtro anterior (ex.: só os ativos, só os com torneio público).
    /// </summary>
    public static IQueryable<HomeGameDistance> Nearby(IQueryable<HomeGame> homeGames, double latitude, double longitude, double radiusKm = DefaultRadiusKm)
    {
        if (radiusKm <= 0)
            radiusKm = DefaultRadiusKm;
        // Caixa que contém o círculo (1° de latitude ≈ 111,32 km; a longitude encolhe com o cosseno da latitude)
        var dLat = radiusKm / 111.32;
        var cos = Math.Max(0.01, Math.Cos(ToRadians(latitude)));
        var dLng = radiusKm / (111.32 * cos);
        var minLat = latitude - dLat;
        var maxLat = latitude + dLat;
        var minLng = longitude - dLng;
        var maxLng = longitude + dLng;
        var latRad = ToRadians(latitude);
        var lngRad = ToRadians(longitude);
        const double toRad = Math.PI / 180;

        return homeGames
            .Where(h => h.Latitude != null && h.Longitude != null
                        && h.Latitude >= minLat && h.Latitude <= maxLat
                        && h.Longitude >= minLng && h.Longitude <= maxLng)
            .Select(h => new HomeGameDistance
            {
                HomeGame = h,
                DistanceKm = 2 * EarthRadiusKm * Math.Asin(Math.Sqrt(
                    Math.Pow(Math.Sin((h.Latitude!.Value * toRad - latRad) / 2), 2)
                    + Math.Cos(latRad) * Math.Cos(h.Latitude!.Value * toRad)
                    * Math.Pow(Math.Sin((h.Longitude!.Value * toRad - lngRad) / 2), 2))),
            })
            .Where(x => x.DistanceKm <= radiusKm)
            .OrderBy(x => x.DistanceKm);
    }

    /// <summary>O ponto público do clube: a coordenada arredondada à grade de <see cref="PublicPointStepDegrees"/>.</summary>
    public static (double Latitude, double Longitude) PublicPoint(double latitude, double longitude) =>
        (Math.Round(latitude / PublicPointStepDegrees, MidpointRounding.AwayFromZero) * PublicPointStepDegrees,
         Math.Round(longitude / PublicPointStepDegrees, MidpointRounding.AwayFromZero) * PublicPointStepDegrees);

    /// <summary>Distância (km, sem arredondar) do visitante ao PONTO PÚBLICO do clube — é ela que o filtro do raio usa.</summary>
    public static double PublicKilometers(double visitorLat, double visitorLng, double clubLat, double clubLng)
    {
        var (lat, lng) = PublicPoint(clubLat, clubLng);
        return Kilometers(visitorLat, visitorLng, lat, lng);
    }

    /// <summary>A distância como sai nas respostas: inteiro de km, mínimo 1 ("a cerca de X km").</summary>
    public static int PublicDistanceKm(double km) => Math.Max(1, (int)Math.Round(km, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Lê o cabeçalho <see cref="VisitorLocationHeader"/> ("-23.55,-46.63"). Null quando ausente, fora de formato ou fora
    /// das faixas. As coordenadas servem só à resposta: nunca vão para o banco nem para o log.
    /// </summary>
    public static (double Lat, double Lng)? ParseVisitorLocation(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;
        var parts = header.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
            return null;
        if (double.IsNaN(lat) || double.IsNaN(lng) || lat is < -90 or > 90 || lng is < -180 or > 180)
            return null;
        return (lat, lng);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
