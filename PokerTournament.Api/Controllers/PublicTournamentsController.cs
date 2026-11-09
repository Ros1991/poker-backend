using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Página pública do torneio, sem login (SPEC-20261108-1639): <c>GET public/tournaments/{slug}?lat&amp;lng</c>. Mesma
/// moldura do ranking público: rota sem {homeGameId}/{tournamentId} (o filtro de tenant não age), limite por IP
/// (public-anon) e um cache curto da página montada; a distância do visitante é calculada por chamada e as coordenadas
/// dele nunca são guardadas. Torneio privado, cancelado ou excluído → 404 "não disponível".
/// </summary>
[ApiController]
[Route("api/v1/public/tournaments")]
[AllowAnonymous]
[EnableRateLimiting("public-anon")]
public class PublicTournamentsController : ControllerBase
{
    public const string NotAvailableMessage = "Este torneio não está disponível.";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);
    private const int MaxSlugLength = 80;

    private readonly PublicTournamentService _service;
    private readonly IMemoryCache _cache;

    public PublicTournamentsController(PublicTournamentService service, IMemoryCache cache)
    {
        _service = service;
        _cache = cache;
    }

    public static string CacheKey(string slug) => $"public-tournament:{slug}";

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(PublicTournamentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicTournamentResponse>> GetBySlug(
        string slug, [FromQuery] double? lat, [FromQuery] double? lng, CancellationToken ct)
    {
        var key = slug.Trim().ToLowerInvariant();
        if (key.Length == 0 || key.Length > MaxSlugLength)
            return NotFound(new { message = NotAvailableMessage });

        var cacheKey = CacheKey(key);
        if (!_cache.TryGetValue(cacheKey, out PublicTournamentData? data))
        {
            data = await _service.GetBySlugAsync(key, ct);
            // Só guarda torneio encontrado: slug inventado não ocupa memória
            if (data is not null)
                _cache.Set(cacheKey, data, CacheDuration);
        }
        if (data is null)
            return NotFound(new { message = NotAvailableMessage });

        var distance = PublicTournamentService.Distance(data, lat, lng);
        if (distance is null)
        {
            Response.Headers.CacheControl = "public, max-age=10";
            return Ok(data.Response);
        }
        // Com a localização do visitante a resposta é dele: nada de cache compartilhado
        Response.Headers.CacheControl = "no-store";
        var personal = Clone(data.Response);
        personal.DistanceKm = distance;
        return Ok(personal);
    }

    private static PublicTournamentResponse Clone(PublicTournamentResponse source) =>
        (PublicTournamentResponse)source.MemberwiseCloneForDistance();
}

internal static class PublicTournamentResponseExtensions
{
    /// <summary>Cópia rasa só para pôr a distância sem mexer no objeto do cache.</summary>
    public static object MemberwiseCloneForDistance(this PublicTournamentResponse source) =>
        new PublicTournamentResponse
        {
            TournamentId = source.TournamentId,
            Slug = source.Slug,
            Name = source.Name,
            Description = source.Description,
            Date = source.Date,
            Status = source.Status,
            HasStarted = source.HasStarted,
            IsOver = source.IsOver,
            ClubName = source.ClubName,
            District = source.District,
            City = source.City,
            State = source.State,
            LocationSummary = source.LocationSummary,
            DistanceKm = source.DistanceKm,
            BuyInAmount = source.BuyInAmount,
            RebuyAmount = source.RebuyAmount,
            AddonAmount = source.AddonAmount,
            MaxRebuys = source.MaxRebuys,
            AddonAllowed = source.AddonAllowed,
            MaxAddons = source.MaxAddons,
            StartingStack = source.StartingStack,
            RebuyStack = source.RebuyStack,
            AddonStack = source.AddonStack,
            KnockoutType = source.KnockoutType,
            BountyAmount = source.BountyAmount,
            TimebankCount = source.TimebankCount,
            TimebankSeconds = source.TimebankSeconds,
            StructureName = source.StructureName,
            LevelCount = source.LevelCount,
            FirstLevel = source.FirstLevel,
            TypicalLevelMinutes = source.TypicalLevelMinutes,
            MaxPlayers = source.MaxPlayers,
            ConfirmedPlayers = source.ConfirmedPlayers,
            SpotsLeft = source.SpotsLeft,
            EstimatedPrizePool = source.EstimatedPrizePool,
            FullHousePrizePool = source.FullHousePrizePool,
            OrganizerName = source.OrganizerName,
            OrganizerPhotoUrl = source.OrganizerPhotoUrl,
            OrganizerWhatsapp = source.OrganizerWhatsapp,
            Plan = source.Plan,
            ShowBadge = source.ShowBadge,
            ClubLogoUrl = source.ClubLogoUrl,
        };
}
