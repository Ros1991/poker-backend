using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Api.Authorization;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Histórico do torneio (terminal do dealer e painel): tudo que aconteceu, com horário e
/// quem fez, e o "Desfazer" de inscrição, rebuy, add-on, pagamento, abatimento e eliminação.
/// </summary>
[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/history")]
[Authorize]
public class TournamentHistoryController : ControllerBase
{
    private readonly TournamentHistoryService _historyService;

    public TournamentHistoryController(TournamentHistoryService historyService)
    {
        _historyService = historyService;
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpGet]
    public async Task<ActionResult<List<HistoryItemResponse>>> Get(
        Guid tournamentId, [FromQuery] Guid? entryId, CancellationToken ct)
    {
        var result = await _historyService.GetHistoryAsync(tournamentId, entryId, ct);
        return Ok(result);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("transactions/{transactionId:guid}/undo")]
    public async Task<ActionResult<UndoResultResponse>> UndoTransaction(
        Guid tournamentId, Guid transactionId, CancellationToken ct)
    {
        // Estorno de pagamento/abatimento exige Payments no plano do dono (402); só o Admin do sistema pula — o token
        // de dealer (escopo de torneio) nunca
        var systemAdmin = User.IsInRole("Admin") && string.IsNullOrEmpty(User.FindFirst(TokenKinds.ClaimType)?.Value);
        var message = await _historyService.UndoTransactionAsync(
            tournamentId, transactionId, GetUserId(), bypassPlanLimits: systemAdmin, ct: ct);
        return Ok(new UndoResultResponse { Message = message });
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("eliminations/{eliminationId:guid}/undo")]
    public async Task<ActionResult<UndoResultResponse>> UndoElimination(
        Guid tournamentId, Guid eliminationId, CancellationToken ct)
    {
        var message = await _historyService.UndoEliminationAsync(tournamentId, eliminationId, GetUserId(), ct);
        return Ok(new UndoResultResponse { Message = message });
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}
