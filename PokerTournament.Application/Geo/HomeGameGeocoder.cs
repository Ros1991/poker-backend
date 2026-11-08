using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Geo;

/// <summary>Como terminou um trabalho de geocodificação.</summary>
public enum GeocodeOutcome
{
    /// <summary>Coordenadas gravadas.</summary>
    Located,
    /// <summary>Nenhum nível achou: GeocodeFailedAt gravado, sem nova tentativa (a resposta do provedor foi válida).</summary>
    NotFound,
    /// <summary>O provedor não respondeu: quem chama agenda nova tentativa.</summary>
    Unavailable,
    /// <summary>Home game inexistente, sem endereço geocodificável ou já com coordenadas: nada a fazer.</summary>
    Skipped,
}

/// <summary>
/// Um trabalho da fila (SPEC-20261108-1634): lê o home game, monta os candidatos por precisão
/// (<see cref="HomeGameAddress.Candidates"/>), consulta o cache <c>geocode_cache</c> antes do provedor, grava o resultado
/// no cache (achado ou não) e as coordenadas no home game. Scoped (um DbContext por trabalho).
/// </summary>
public class HomeGameGeocoder
{
    private readonly IAppDbContext _db;
    private readonly IGeocoder _geocoder;
    private readonly ILogger<HomeGameGeocoder> _logger;

    public HomeGameGeocoder(IAppDbContext db, IGeocoder geocoder, ILogger<HomeGameGeocoder> logger)
    {
        _db = db;
        _geocoder = geocoder;
        _logger = logger;
    }

    public async Task<GeocodeOutcome> ProcessAsync(Guid homeGameId, CancellationToken ct = default)
    {
        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == homeGameId, ct);
        if (homeGame is null || homeGame.Latitude is not null)
            return GeocodeOutcome.Skipped;
        var candidates = HomeGameAddress.Candidates(homeGame);
        if (candidates.Count == 0)
            return GeocodeOutcome.Skipped;

        foreach (var candidate in candidates)
        {
            var key = HomeGameAddress.CacheKey(candidate.Query);
            var cached = await _db.GeocodeCache.FirstOrDefaultAsync(g => g.QueryKey == key, ct);
            GeocodePoint? point;
            if (cached is not null)
            {
                point = cached.Found && cached.Latitude is { } la && cached.Longitude is { } lo
                    ? new GeocodePoint(la, lo, cached.ResolvedName)
                    : null;
            }
            else
            {
                GeocodeResult result;
                try
                {
                    result = await _geocoder.GeocodeAsync(candidate.Query, ct);
                }
                catch (GeocoderUnavailableException ex)
                {
                    _logger.LogWarning("Geocodificação adiada do home game {HomeGameId} ({Precision}): {Message}", homeGameId, candidate.Precision, ex.Message);
                    return GeocodeOutcome.Unavailable;
                }
                point = result.Found ? result.Point : null;
                await StoreAsync(key, point, ct);
            }

            if (point is not null)
            {
                homeGame.Latitude = point.Latitude;
                homeGame.Longitude = point.Longitude;
                homeGame.GeocodedAt = DateTimeOffset.UtcNow;
                homeGame.GeocodePrecision = candidate.Precision.ToString();
                homeGame.GeocodeFailedAt = null;
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Home game {HomeGameId} localizado com precisão {Precision}", homeGameId, candidate.Precision);
                return GeocodeOutcome.Located;
            }
        }

        homeGame.GeocodeFailedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Home game {HomeGameId}: endereço não achado pelo geocodificador em nenhum nível", homeGameId);
        return GeocodeOutcome.NotFound;
    }

    /// <summary>Desistiu depois das tentativas: marca a falha para a tela avisar (a varredura da subida tenta de novo em 24 h).</summary>
    public async Task MarkFailedAsync(Guid homeGameId, CancellationToken ct = default)
    {
        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == homeGameId, ct);
        if (homeGame is null || homeGame.Latitude is not null)
            return;
        homeGame.GeocodeFailedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Home games com endereço geocodificável e sem coordenadas que ainda não desistiram (ou desistiram há mais de 24 h):
    /// o que ficou pendente num reinício ou falhou de vez da última vez.
    /// </summary>
    public async Task<List<Guid>> PendingIdsAsync(CancellationToken ct = default)
    {
        var retryBefore = DateTimeOffset.UtcNow.AddHours(-24);
        var rows = await _db.HomeGames.AsNoTracking()
            .Where(h => h.Latitude == null && (h.PostalCode != null || (h.Street != null && h.City != null && h.State != null)))
            .Where(h => h.GeocodeFailedAt == null || h.GeocodeFailedAt < retryBefore)
            .OrderBy(h => h.UpdatedAt)
            .Select(h => h.Id)
            .ToListAsync(ct);
        return rows;
    }

    private async Task StoreAsync(string key, GeocodePoint? point, CancellationToken ct)
    {
        _db.GeocodeCache.Add(new GeocodeCacheEntry
        {
            QueryKey = key,
            Found = point is not null,
            Latitude = point?.Latitude,
            Longitude = point?.Longitude,
            Provider = _geocoder.ProviderName,
            ResolvedName = point?.ResolvedName is { Length: > 500 } name ? name[..500] : point?.ResolvedName,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Corrida com outro trabalho que gravou a mesma chave: a linha dele vale; a entidade nova sai do contexto
            var entry = _db.GeocodeCache.Local.FirstOrDefault(g => g.QueryKey == key);
            if (entry is not null)
                _db.GeocodeCache.Entry(entry).State = EntityState.Detached;
        }
    }
}
