using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Geo;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Api.Development;

/// <summary>
/// Apoio dos testes da geocodificação (SPEC-20261108-1634) — rotas SÓ em Development (o Program.cs só as mapeia lá),
/// para qualquer conta logada: a busca por raio (<see cref="GeoDistance.Nearby"/>, que a SPEC-20261108-1639 vai expor de
/// verdade) e o tamanho da fila. Nada aqui existe fora de Development.
/// </summary>
public static class DevelopmentGeoEndpoints
{
    public static void MapDevelopmentGeoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/dev/geo")
            .RequireAuthorization()
            .WithTags("Geo (somente Development)");

        group.MapGet("/nearby", async (double lat, double lng, double? radiusKm, IAppDbContext db, CancellationToken ct) =>
        {
            var rows = await GeoDistance.Nearby(db.HomeGames.AsNoTracking(), lat, lng, radiusKm ?? GeoDistance.DefaultRadiusKm)
                .Select(x => new { homeGameId = x.HomeGame.Id, name = x.HomeGame.Name, city = x.HomeGame.City, distanceKm = x.DistanceKm })
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        group.MapGet("/queue", (GeocodingQueue queue) => Results.Ok(new { pending = queue.Pending }));
    }
}
