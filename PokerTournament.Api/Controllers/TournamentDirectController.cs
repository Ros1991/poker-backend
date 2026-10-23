using Microsoft.AspNetCore.Authorization;
using PokerTournament.Api.Authorization;
using PokerTournament.Application.Common;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;
using PokerTournament.Api.Filters;
using PokerTournament.Application.Plans;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Rotas diretas para torneios (sem necessidade de homeGameId na URL).
/// Usado pelo frontend quando já se tem o tournamentId.
/// O parâmetro de rota precisa se chamar {tournamentId}: é por ele que o
/// TenantAccessFilter exige dono/membro (com {id} o filtro não agia).
/// </summary>
[ApiController]
[Route("api/v1/tournaments")]
[Authorize]
public class TournamentDirectController : ControllerBase
{
    private readonly TournamentService _tournamentService;

    public TournamentDirectController(TournamentService tournamentService)
    {
        _tournamentService = tournamentService;
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpGet("{tournamentId:guid}")]
    public async Task<ActionResult<TournamentResponse>> GetById(Guid tournamentId, CancellationToken ct)
    {
        var result = await _tournamentService.GetByIdAsync(tournamentId, ct);
        if (result is null)
            return NotFound(new { message = "Torneio não encontrado." });
        return Ok(result);
    }

    [HttpPut("{tournamentId:guid}/punctuality-bonus")]
    public async Task<ActionResult<TournamentResponse>> UpdatePunctualityBonus(
        Guid tournamentId, [FromBody] UpdatePunctualityBonusRequest request, CancellationToken ct)
    {
        var result = await _tournamentService.UpdatePunctualityBonusAsync(
            tournamentId, request.Count, request.Chips, ct);
        return Ok(result);
    }

    // PIX do dia é financeiro (D3): 402 feature_locked no Mesa
    [RequirePlanFeature(PlanFeature.Payments)]
    [HttpPut("{tournamentId:guid}/pix-key")]
    public async Task<ActionResult<TournamentResponse>> UpdatePixKey(
        Guid tournamentId, [FromBody] UpdatePixKeyRequest request, CancellationToken ct)
    {
        var result = await _tournamentService.UpdatePixKeyAsync(tournamentId, request.PixKey, ct);
        return Ok(result);
    }
}
