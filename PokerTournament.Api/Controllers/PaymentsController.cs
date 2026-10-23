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
    public async Task<ActionResult> GetTotalsByMethod(
        Guid tournamentId, CancellationToken ct)
    {
        // Inclui abatimentos (Settlement): não entram em PIX/Dinheiro, mas creditam
        // um custo (PixDestinationId), então devem aparecer em "Pago para Custos".
        var transactions = await _db.Transactions
            .Where(t => t.TournamentId == tournamentId
                     && (t.Type == nameof(TransactionType.Payment)
                         || t.Type == nameof(TransactionType.Settlement))
                     && t.ReversedAt == null)
            .ToListAsync(ct);

        decimal totalPix = 0;
        decimal totalCash = 0;
        decimal totalToCosts = 0;

        foreach (var t in transactions)
        {
            if (t.PaymentMethod == "Mixed")
            {
                totalPix += t.PixAmount ?? 0;
                totalCash += t.CashAmount ?? 0;
            }
            else if (t.PaymentMethod == "Pix")
            {
                totalPix += t.Amount;
            }
            else if (t.PaymentMethod == "Dinheiro")
            {
                totalCash += t.Amount;
            }

            if (t.PixDestinationId.HasValue)
            {
                // Pagamento para um custo extra (que nao seja Caixa)
                var cost = await _db.CostExtras
                    .FirstOrDefaultAsync(c => c.Id == t.PixDestinationId, ct);
                if (cost != null && !cost.IsCashBox)
                    totalToCosts += t.Amount;
            }
        }

        var totalDue = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournamentId)
            .SumAsync(e => (decimal?)e.TotalDue, ct) ?? 0;
        var totalPaid = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournamentId)
            .SumAsync(e => (decimal?)e.TotalPaid, ct) ?? 0;
        var totalPending = totalDue - totalPaid;

        return Ok(new
        {
            totalPix,
            totalCash,
            totalToCosts,
            totalPending,
        });
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
