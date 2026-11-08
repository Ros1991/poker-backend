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

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
