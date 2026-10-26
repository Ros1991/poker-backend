using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Ranking público, sem login (página /r/{slug} do frontend).
/// A rota não tem {homeGameId} nem {tournamentId}: com eles o TenantAccessFilter
/// devolveria 403 para quem está logado mas não é membro do home game.
/// </summary>
[ApiController]
[Route("api/v1/public/rankings")]
[AllowAnonymous]
public class PublicRankingsController : ControllerBase
{
    // Link circula por WhatsApp: segura picos de acesso sem recalcular a cada request
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private const int MaxSlugLength = 120;

    private readonly RankingLeaderboardService _leaderboard;
    private readonly IMemoryCache _cache;

    public PublicRankingsController(RankingLeaderboardService leaderboard, IMemoryCache cache)
    {
        _leaderboard = leaderboard;
        _cache = cache;
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<PublicRankingResponse>> GetBySlug(string slug, CancellationToken ct)
    {
        var key = slug.Trim().ToLowerInvariant();
        if (key.Length == 0 || key.Length > MaxSlugLength)
            return NotFound(new { message = "Ranking não encontrado." });

        var cacheKey = RankingLeaderboardService.PublicCacheKey(key);
        if (!_cache.TryGetValue(cacheKey, out PublicRankingResponse? response))
        {
            response = await _leaderboard.GetPublicBySlugAsync(key, ct);
            // Só guarda ranking encontrado: slug inventado não ocupa memória
            if (response is not null)
                _cache.Set(cacheKey, response, CacheDuration);
        }

        if (response is null)
            return NotFound(new { message = "Ranking não encontrado." });

        Response.Headers.CacheControl = "public, max-age=30";
        return Ok(response);
    }
}
