using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Endpoints standalone para ranking (sem precisar de homeGameId na URL).
/// Sem tenant na rota o TenantAccessFilter não age: cada ação confere o home game do ranking
/// (dono/membro/Admin). A página pública continua por slug em PublicRankingsController.
/// Ranking de temporada é recurso do plano pago (SPEC-20261023-1205): depois do acesso, o plano do
/// dono do home game do ranking precisa ter Rankings (402 feature_locked; Admin passa) — como o
/// [RequirePlanFeature] de rankings/* aninhado, que aqui não acha home game na rota.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class RankingsController : HomeGameScopedControllerBase
{
    private readonly RankingLeaderboardService _leaderboard;
    private readonly RankingSlugService _slugs;
    private readonly PlanService _plans;

    public RankingsController(
        IAppDbContext db, RankingLeaderboardService leaderboard, RankingSlugService slugs, PlanService plans)
        : base(db)
    {
        _leaderboard = leaderboard;
        _slugs = slugs;
        _plans = plans;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> GetById(Guid id, CancellationToken ct)
    {
        var ranking = await _db.Rankings
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (ranking is null)
            return NotFound(new { message = "Ranking não encontrado." });

        // Antes da geração de slug: quem não tem acesso não dispara escrita
        if (!await CanAccessAsync(ranking.HomeGameId, ct))
            return Forbidden();
        await _plans.EnsureFeatureForHomeGameAsync(ranking.HomeGameId, PlanFeature.Rankings, bypass: IsAdmin, ct: ct);

        // Rede de segurança: se o backfill do boot falhou (ou o ranking veio de uma
        // instância antiga durante o deploy), gera o endereço público ao abrir a tela.
        if (ranking.Slug is null)
        {
            try
            {
                ranking.Slug = await _slugs.NextAvailableAsync(ranking.Name, ct);
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Outra requisição gerou ao mesmo tempo; na próxima abertura ele aparece
                ranking.Slug = null;
            }
        }

        return Ok(new
        {
            ranking.Id,
            ranking.HomeGameId,
            ranking.Name,
            ranking.Slug,
            ranking.Description,
            ranking.Season,
            ranking.IsActive,
            ranking.ScoringMode,
            ranking.ScoringFormula,
            ranking.ScoringTable,
            ranking.AccumulatedPrize,
            ranking.DiscardCount,
            ranking.CreatedAt
        });
    }

    [HttpGet("{id:guid}/leaderboard")]
    public async Task<ActionResult<List<RankingLeaderboardResponse>>> GetLeaderboard(
        Guid id, CancellationToken ct)
    {
        var ranking = await _db.Rankings
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ranking is null)
            return NotFound(new { message = "Ranking não encontrado." });

        if (!await CanAccessAsync(ranking.HomeGameId, ct))
            return Forbidden();
        await _plans.EnsureFeatureForHomeGameAsync(ranking.HomeGameId, PlanFeature.Rankings, bypass: IsAdmin, ct: ct);

        return Ok(await _leaderboard.GetLeaderboardAsync(ranking, ct));
    }
}
