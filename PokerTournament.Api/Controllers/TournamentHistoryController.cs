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
        var result = await _historyService.GetHistoryAsync(tournamentId, entryId, IsOrganizer(), ct);
        return Ok(result);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("transactions/{transactionId:guid}/undo")]
    public async Task<ActionResult<UndoResultResponse>> UndoTransaction(
        Guid tournamentId, Guid transactionId, CancellationToken ct)
    {
        // Estorno de pagamento/abatimento exige Payments no plano do dono (402) para todos: token de dealer e Admin do
        // sistema inclusive (SPEC-20261026-1126)
        // Organizador desfaz rebuy/add-on/inscrição mesmo com torneio encerrado ou prêmio pago (SPEC-20261023-1217, L4)
        var message = await _historyService.UndoTransactionAsync(
            tournamentId, transactionId, GetUserId(), asOrganizer: IsOrganizer(), ct: ct);
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

    /// <summary>
    /// Token de usuário (sem o claim "kind"; mesma regra do IsScopedToken de EntriesController): quem chega aqui com
    /// ele já passou no TenantAccessFilter (dono, membro ou Admin). Token de dealer (QR) → false. A regra é pelo tipo
    /// de token, não pela tela: o organizador logado no terminal tem as mesmas permissões do painel.
    /// </summary>
    private bool IsOrganizer() =>
        string.IsNullOrEmpty(User.FindFirst(TokenKinds.ClaimType)?.Value);

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}
