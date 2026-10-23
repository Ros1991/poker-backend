using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Dados da conta logada. Sem [AllowTokenKind]: o token de dealer (QR) não chega aqui (403).
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly PlanService _plans;

    public MeController(PlanService plans)
    {
        _plans = plans;
    }

    /// <summary>
    /// Plano da conta (SPEC-20261023-1205): plano gravado e efetivo, vencimento, origem, nota, limites e
    /// recursos do plano efetivo, uso (home games n/limite, torneios nos últimos 7 dias n/limite + quando
    /// libera o próximo, membros por home game) e o catálogo inteiro (tabela comparativa de "Meu plano").
    /// </summary>
    [HttpGet("plan")]
    public async Task<ActionResult<MyPlanResponse>> GetPlan(CancellationToken ct)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        if (!Guid.TryParse(claim?.Value, out var userId))
            return NotFound(new { message = "Usuário não encontrado." });

        var plan = await _plans.GetMyPlanAsync(userId, ct);
        return plan is null ? NotFound(new { message = "Usuário não encontrado." }) : Ok(plan);
    }
}
