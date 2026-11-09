using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Geo;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// "Torneios perto de mim" (SPEC-20261108-1639): <c>GET me/tournaments/near?radiusKm=30|100&amp;maxBuyIn&amp;weekday&amp;format&amp;page</c>,
/// conta logada. A localização do navegador vem pelo cabeçalho <c>X-Visitor-Location: lat,lng</c> (nunca na query string,
/// que fica em log e histórico) e serve só a esta chamada (nada é guardado); sem ela, a cidade/UF do perfil.
/// Sem [AllowTokenKind]: o dealer por QR não busca (403).
/// </summary>
[ApiController]
[Route("api/v1/me/tournaments")]
[Authorize]
public class NearbyTournamentsController : ControllerBase
{
    private readonly NearbyTournamentsService _service;

    public NearbyTournamentsController(NearbyTournamentsService service)
    {
        _service = service;
    }

    [HttpGet("near")]
    [ProducesResponseType(typeof(NearbyTournamentsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<NearbyTournamentsResponse>> Near(
        [FromHeader(Name = GeoDistance.VisitorLocationHeader)] string? visitorLocation,
        [FromQuery] double? radiusKm, [FromQuery] decimal? maxBuyIn,
        [FromQuery] int? weekday, [FromQuery] string? format, [FromQuery] int page = 1, [FromQuery] int pageSize = NearbyQuery.DefaultPageSize,
        CancellationToken ct = default)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        if (!Guid.TryParse(claim?.Value, out var userId))
            return NotFound(new { message = "Usuário não encontrado." });
        var visitor = GeoDistance.ParseVisitorLocation(visitorLocation);
        var result = await _service.SearchAsync(userId, new NearbyQuery
        {
            Latitude = visitor?.Lat,
            Longitude = visitor?.Lng,
            RadiusKm = radiusKm,
            MaxBuyIn = maxBuyIn,
            Weekday = weekday,
            Format = format,
            Page = page,
            PageSize = pageSize,
        }, ct);
        // Resposta pessoal (localização do navegador ou cidade do perfil): nunca em cache compartilhado
        Response.Headers.CacheControl = "no-store";
        return Ok(result);
    }
}
