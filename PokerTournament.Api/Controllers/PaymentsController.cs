using System.Security.Claims;
using PokerTournament.Api.Authorization;
using PokerTournament.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Enums;
using PokerTournament.Api.Filters;
using PokerTournament.Application.Plans;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/[controller]")]
[Authorize]
// SPEC-20261023-1205: recurso exclusivo do plano pago (402 feature_locked no Mesa, inclusive para o token de dealer)
[RequirePlanFeature(PlanFeature.Payments)]
public class PaymentsController : ControllerBase
{
    private readonly PaymentService _paymentService;
    private readonly IAppDbContext _db;

    public PaymentsController(PaymentService paymentService, IAppDbContext db)
    {
        _paymentService = paymentService;
        _db = db;
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("entries/{entryId:guid}")]
    public async Task<ActionResult<EntryResponse>> RegisterPayment(
        Guid tournamentId, Guid entryId, [FromBody] CreatePaymentRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _paymentService.RegisterPaymentAsync(tournamentId, entryId, request, userId, ct);
        return Ok(result);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("entries/{entryId:guid}/settle-against-cost")]
    public async Task<ActionResult<EntryResponse>> SettleAgainstCost(
        Guid tournamentId, Guid entryId, [FromBody] SettleAgainstCostRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _paymentService.SettleAgainstCostAsync(
            tournamentId, entryId, request.CostExtraId, request.Amount, userId, ct);
        return Ok(result);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpGet("entries/{entryId:guid}")]
    public async Task<ActionResult> GetByEntry(
        Guid tournamentId, Guid entryId, CancellationToken ct)
    {
        var payments = await _db.Transactions
            .Where(t => t.TournamentId == tournamentId
                     && t.EntryId == entryId
                     && (t.Type == nameof(TransactionType.Payment)
                         || t.Type == nameof(TransactionType.Settlement))
                     && t.ReversedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.Amount,
                t.PaymentMethod,
                t.PixAmount,
                t.CashAmount,
                t.PixDestinationId,
                t.Description,
                t.CreatedAt
            })
            .ToListAsync(ct);
        return Ok(payments);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("entries/{entryId:guid}/{paymentId:guid}/reverse")]
    public async Task<ActionResult> ReversePayment(
        Guid tournamentId, Guid entryId, Guid paymentId, CancellationToken ct)
    {
        var userId = GetUserId();
        await _paymentService.ReversePaymentAsync(tournamentId, entryId, paymentId, userId, ct: ct);
        return Ok(new { message = "Pagamento estornado." });
    }

    [HttpGet("totals-by-method")]
    public async Task<ActionResult<PaymentTotalsByMethodResponse>> GetTotalsByMethod(
        Guid tournamentId, CancellationToken ct)
    {
        // Mesma conta do detalhamento (SPEC-20261026-1906): o misto credita o custo só com a parte PIX e os
        // abatimentos entram em "Pago para Custos"
        var result = await _paymentService.GetTotalsByMethodAsync(tournamentId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Detalhamento financeiro (SPEC-20261026-1906): resumo por forma de pagamento e, para o caixa e cada custo, quem
    /// pagou (jogador, valor, forma, hora). Só o organizador (aba Financeiro); o token de dealer não chega aqui.
    /// </summary>
    [HttpGet("breakdown")]
    public async Task<ActionResult<PaymentBreakdownResponse>> GetBreakdown(
        Guid tournamentId, CancellationToken ct)
    {
        var result = await _paymentService.GetPaymentBreakdownAsync(tournamentId, ct);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<PaymentSummaryResponse>> GetSummary(
        Guid tournamentId, CancellationToken ct)
    {
        var result = await _paymentService.GetPaymentSummaryAsync(tournamentId, ct);
        return Ok(result);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpGet("pix-suggestions")]
    public async Task<ActionResult<List<PixSuggestionResponse>>> GetPixSuggestions(
        Guid tournamentId, [FromQuery] decimal amount, CancellationToken ct)
    {
        var result = await _paymentService.GetPixSuggestionsAsync(tournamentId, amount, ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}
