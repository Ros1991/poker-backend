using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Geo;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Application.Services;

/// <summary>Filtros da busca "perto de mim".</summary>
public sealed class NearbyQuery
{
    /// <summary>Localização do navegador (usada só nesta resposta, nunca guardada).</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>30 (padrão) ou 100 km ("ampliar").</summary>
    public double? RadiusKm { get; set; }
    public decimal? MaxBuyIn { get; set; }
    /// <summary>Dia da semana (0 = domingo … 6 = sábado), na hora de Brasília.</summary>
    public int? Weekday { get; set; }
    /// <summary>None | Knockout | Simple | Progressive.</summary>
    public string? Format { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;

    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
}

/// <summary>
/// "Torneios perto de mim" (SPEC-20261108-1639, D4): torneios PÚBLICOS, de hoje em diante, não cancelados/finalizados, de
/// clubes ativos e geocodificados, dentro do raio (30 km; 100 com um toque) pelo <see cref="GeoDistance.Nearby"/>,
/// ordenados por data. A localização é a do navegador (quando a pessoa permitiu, só nesta chamada) ou a cidade/UF do
/// perfil, geocodificada pelo mesmo <see cref="IGeocoder"/> com cache em <c>geocode_cache</c>. Sem nenhuma das duas, a
/// resposta pede para completar o perfil. Filtros: buy-in máximo, dia da semana e formato. Os torneios dos clubes que a
/// conta acessa vêm marcados "seu clube"; o pedido aberto dela e a inscrição também.
/// </summary>
public class NearbyTournamentsService
{
    public const double WideRadiusKm = 100;
    /// <summary>Teto de torneios lidos do banco antes dos filtros em memória (dia da semana) e da paginação.</summary>
    private const int MaxRows = 500;

    private readonly IAppDbContext _db;
    private readonly IGeocoder _geocoder;
    private readonly PlanService _plans;
    private readonly ILogger<NearbyTournamentsService> _logger;

    public NearbyTournamentsService(IAppDbContext db, IGeocoder geocoder, PlanService plans, ILogger<NearbyTournamentsService> logger)
    {
        _db = db;
        _geocoder = geocoder;
        _plans = plans;
        _logger = logger;
    }

    public async Task<NearbyTournamentsResponse> SearchAsync(Guid userId, NearbyQuery query, CancellationToken ct = default)
    {
        var radius = query.RadiusKm is > GeoDistance.DefaultRadiusKm ? WideRadiusKm : GeoDistance.DefaultRadiusKm;
        var paging = Paging.Normalize(query.Page, query.PageSize, NearbyQuery.DefaultPageSize, NearbyQuery.MaxPageSize);
        var response = new NearbyTournamentsResponse { RadiusKm = radius, Page = paging.Page, PageSize = paging.PageSize };

        // 1. De onde: navegador (coordenadas válidas) ou a cidade/UF do perfil
        double lat, lng;
        if (query.Latitude is { } la && query.Longitude is { } lo && la is >= -90 and <= 90 && lo is >= -180 and <= 180)
        {
            lat = la;
            lng = lo;
            response.LocationSource = "browser";
            response.LocationLabel = "Sua localização";
        }
        else
        {
            var profile = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.City, u.State })
                .FirstOrDefaultAsync(ct);
            if (profile is null || string.IsNullOrWhiteSpace(profile.City) || string.IsNullOrWhiteSpace(profile.State))
                return response; // "none": a tela pede para completar o perfil
            response.LocationSource = "profile";
            response.LocationLabel = $"{profile.City.Trim()} - {profile.State.Trim().ToUpperInvariant()}";
            var point = await GeocodeCityAsync(profile.City.Trim(), profile.State.Trim().ToUpperInvariant(), ct);
            if (point is null)
            {
                response.GeocodeFailed = true;
                return response;
            }
            (lat, lng) = point.Value;
        }

        // 2. Clubes no raio (distância calculada no banco) e os torneios públicos futuros deles
        var nearby = await GeoDistance.Nearby(_db.HomeGames.AsNoTracking().Where(h => h.IsActive), lat, lng, radius)
            .Select(x => new { x.HomeGame.Id, x.DistanceKm })
            .ToListAsync(ct);
        if (nearby.Count == 0)
            return response;
        var distances = nearby.ToDictionary(x => x.Id, x => x.DistanceKm);
        var homeGameIds = distances.Keys.ToList();

        var brtNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3));
        var todayStart = new DateTimeOffset(brtNow.Date, TimeSpan.FromHours(-3));
        var cancelled = nameof(TournamentStatus.Cancelled);
        var finished = nameof(TournamentStatus.Finished);
        var tournaments = _db.Tournaments.AsNoTracking()
            .Where(t => t.IsPublic && t.PublicSlug != null && homeGameIds.Contains(t.HomeGameId)
                && t.Date >= todayStart && t.Status != cancelled && t.Status != finished);
        if (query.MaxBuyIn is { } maxBuyIn && maxBuyIn >= 0)
            tournaments = tournaments.Where(t => t.BuyInAmount <= maxBuyIn);
        var format = NormalizeFormat(query.Format);
        if (format == "Knockout")
            tournaments = tournaments.Where(t => t.KnockoutType != nameof(KnockoutType.None));
        else if (format is not null)
            tournaments = tournaments.Where(t => t.KnockoutType == format);

        var rows = await tournaments
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .Take(MaxRows)
            .Select(t => new
            {
                t.Id, t.PublicSlug, t.Name, t.Date, t.Status, t.BuyInAmount, t.RebuyAmount, t.AddonAmount, t.AddonAllowed,
                t.KnockoutType, t.MaxPlayers, t.HomeGameId,
                ClubName = t.HomeGame.Name, ClubIsPrimary = t.HomeGame.IsPrimary, OwnerId = t.HomeGame.OwnerId, OwnerName = t.HomeGame.Owner.FullName,
                t.HomeGame.District, t.HomeGame.City, t.HomeGame.State,
            })
            .ToListAsync(ct);
        if (query.Weekday is { } weekday && weekday is >= 0 and <= 6)
            rows = rows.Where(t => (int)t.Date.ToOffset(TimeSpan.FromHours(-3)).DayOfWeek == weekday).ToList();

        var ordered = rows.OrderBy(t => t.Date).ThenBy(t => distances[t.HomeGameId]).ThenBy(t => t.Id).ToList();
        response.Total = ordered.Count;
        if (paging.IsPastEnd(ordered.Count))
            return response;
        var page = ordered.Skip(paging.Skip).Take(paging.PageSize).ToList();
        var pageIds = page.Select(t => t.Id).ToList();

        // 3. Vagas, "seu clube", meu pedido e minha inscrição — consultas agrupadas só para a página
        var entryCounts = await _db.TournamentEntries.AsNoTracking()
            .Where(e => pageIds.Contains(e.TournamentId))
            .GroupBy(e => e.TournamentId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var approved = JoinRequestService.Approved;
        var reservationCounts = await _db.TournamentJoinRequests.AsNoTracking()
            .Where(r => pageIds.Contains(r.TournamentId) && r.Status == approved && r.EntryId == null)
            .GroupBy(r => r.TournamentId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var openStatuses = JoinRequestService.OpenStatuses;
        var myRequests = await _db.TournamentJoinRequests.AsNoTracking()
            .Where(r => pageIds.Contains(r.TournamentId) && r.UserId == userId && openStatuses.Contains(r.Status))
            .Select(r => new { r.TournamentId, r.Status })
            .ToDictionaryAsync(x => x.TournamentId, x => x.Status, ct);
        var registered = (await _db.TournamentEntries.AsNoTracking()
            .Where(e => pageIds.Contains(e.TournamentId) && e.Person.UserId == userId)
            .Select(e => e.TournamentId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();
        var myClubs = (await HomeGameAccess.AccessibleHomeGameIdsAsync(_db, userId, ct)).ToHashSet();
        var ownerPlans = new Dictionary<Guid, string>();
        foreach (var ownerId in page.Select(t => t.OwnerId).Distinct())
            ownerPlans[ownerId] = await _plans.GetUserEffectivePlanAsync(ownerId, ct);

        response.Items = page.Select(t =>
        {
            var taken = entryCounts.GetValueOrDefault(t.Id) + reservationCounts.GetValueOrDefault(t.Id);
            return new NearbyTournamentItem
            {
                TournamentId = t.Id,
                Slug = t.PublicSlug!,
                Name = t.Name,
                ClubName = HomeGamePrimary.LabelFor(t.ClubIsPrimary, ownerPlans[t.OwnerId], t.ClubName, t.OwnerName),
                District = Clean(t.District),
                City = Clean(t.City),
                State = Clean(t.State),
                LocationSummary = JoinRequestService.LocationSummary(t.District, t.City, t.State),
                DistanceKm = Math.Round(distances[t.HomeGameId], 1),
                Date = t.Date,
                Status = t.Status,
                BuyInAmount = t.BuyInAmount,
                RebuyAmount = t.RebuyAmount is > 0 ? t.RebuyAmount : null,
                AddonAmount = t.AddonAllowed && t.AddonAmount is > 0 ? t.AddonAmount : null,
                KnockoutType = string.IsNullOrEmpty(t.KnockoutType) ? nameof(KnockoutType.None) : t.KnockoutType,
                MaxPlayers = t.MaxPlayers,
                SpotsLeft = t.MaxPlayers is { } max ? Math.Max(0, max - taken) : null,
                IsMyClub = myClubs.Contains(t.HomeGameId),
                MyRequestStatus = myRequests.GetValueOrDefault(t.Id),
                Registered = registered.Contains(t.Id),
            };
        }).ToList();
        return response;
    }

    /// <summary>
    /// Cidade/UF do perfil → coordenadas, pelo cache <c>geocode_cache</c> antes do provedor (a mesma chave normalizada do
    /// endereço do home game). Null = não achado ou provedor fora do ar (a tela avisa, sem guardar nada).
    /// </summary>
    public async Task<(double Lat, double Lng)?> GeocodeCityAsync(string city, string state, CancellationToken ct)
    {
        var query = $"{city}, {state}, Brasil";
        var key = HomeGameAddress.CacheKey(query);
        var cached = await _db.GeocodeCache.AsNoTracking().FirstOrDefaultAsync(g => g.QueryKey == key, ct);
        if (cached is not null)
            return cached.Found && cached.Latitude is { } la && cached.Longitude is { } lo ? (la, lo) : null;

        GeocodeResult result;
        try
        {
            result = await _geocoder.GeocodeAsync(query, ct);
        }
        catch (GeocoderUnavailableException ex)
        {
            _logger.LogWarning("Perto de mim: geocodificação da cidade do perfil adiada ({Query}): {Message}", query, ex.Message);
            return null;
        }
        var point = result.Found ? result.Point : null;
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
            // Corrida com outra consulta igual: a linha dela vale
            var entry = _db.GeocodeCache.Local.FirstOrDefault(g => g.QueryKey == key);
            if (entry is not null)
                _db.GeocodeCache.Entry(entry).State = EntityState.Detached;
        }
        return point is null ? null : (point.Latitude, point.Longitude);
    }

    private static string? NormalizeFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return null;
        var f = format.Trim();
        if (string.Equals(f, "Knockout", StringComparison.OrdinalIgnoreCase)) return "Knockout";
        if (string.Equals(f, nameof(KnockoutType.None), StringComparison.OrdinalIgnoreCase)) return nameof(KnockoutType.None);
        if (string.Equals(f, nameof(KnockoutType.Simple), StringComparison.OrdinalIgnoreCase)) return nameof(KnockoutType.Simple);
        if (string.Equals(f, nameof(KnockoutType.Progressive), StringComparison.OrdinalIgnoreCase)) return nameof(KnockoutType.Progressive);
        return null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
