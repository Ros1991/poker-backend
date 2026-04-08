using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/[controller]")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly PaymentService _paymentService;
    private readonly IAppDbContext _db;

    public PaymentsController(PaymentService paymentService, IAppDbContext db)
    {
        _paymentService = paymentService;
        _db = db;
    }

    [HttpPost("entries/{entryId:guid}")]
    public async Task<ActionResult<EntryResponse>> RegisterPayment(
        Guid tournamentId, Guid entryId, [FromBody] CreatePaymentRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _paymentService.RegisterPaymentAsync(tournamentId, entryId, request, userId, ct);
        return Ok(result);
    }

    [HttpGet("entries/{entryId:guid}")]
    public async Task<ActionResult> GetByEntry(
        Guid tournamentId, Guid entryId, CancellationToken ct)
    {
        var payments = await _db.Transactions
            .Where(t => t.TournamentId == tournamentId
                     && t.EntryId == entryId
                     && t.Type == nameof(TransactionType.Payment)
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

    [HttpPost("entries/{entryId:guid}/{paymentId:guid}/reverse")]
    public async Task<ActionResult> ReversePayment(
        Guid tournamentId, Guid entryId, Guid paymentId, CancellationToken ct)
    {
        var userId = GetUserId();
        var tx = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == paymentId
                                   && t.TournamentId == tournamentId
                                   && t.EntryId == entryId
                                   && t.Type == nameof(TransactionType.Payment), ct);

        if (tx == null)
            return NotFound(new { message = "Pagamento não encontrado." });

        if (tx.ReversedAt != null)
            return BadRequest(new { message = "Pagamento já foi estornado." });

        // Marcar como estornado
        tx.ReversedAt = DateTimeOffset.UtcNow;
        tx.ReversedBy = userId;
        tx.ReversalReason = "Estornado pelo organizador";

        // Atualizar entry
        var entry = await _db.TournamentEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, ct);
        if (entry != null)
        {
            entry.TotalPaid = Math.Max(0, entry.TotalPaid - tx.Amount);
            entry.Balance = entry.TotalDue - entry.TotalPaid;
            entry.PaymentStatus = entry.Balance switch
            {
                0 => nameof(PaymentStatus.Paid),
                > 0 when entry.TotalPaid > 0 => nameof(PaymentStatus.PartiallyPaid),
                < 0 => nameof(PaymentStatus.Overpaid),
                _ => nameof(PaymentStatus.Pending),
            };
        }

        // Se pagou para um custo extra, reverter o status dele
        if (tx.PixDestinationId.HasValue)
        {
            var cost = await _db.CostExtras
                .FirstOrDefaultAsync(c => c.Id == tx.PixDestinationId, ct);
            if (cost != null)
            {
                cost.PaidAmount = Math.Max(0, cost.PaidAmount - tx.Amount);
                if (cost.PaidAmount < cost.Amount)
                {
                    cost.PaymentStatus = nameof(PaymentStatus.Pending);
                    cost.PaidAt = null;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Pagamento estornado." });
    }

    [HttpGet("totals-by-method")]
    public async Task<ActionResult> GetTotalsByMethod(
        Guid tournamentId, CancellationToken ct)
    {
        var transactions = await _db.Transactions
            .Where(t => t.TournamentId == tournamentId
                     && t.Type == nameof(TransactionType.Payment)
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
