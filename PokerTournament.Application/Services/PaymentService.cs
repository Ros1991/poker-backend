using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

public class PaymentService
{
    private readonly IAppDbContext _db;

    public PaymentService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<EntryResponse> RegisterPaymentAsync(
        Guid tournamentId, Guid entryId, CreatePaymentRequest request, Guid? createdBy = null, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        if (request.Amount <= 0)
            throw new DomainException("Valor do pagamento deve ser positivo.");

        var transaction = new Transaction
        {
            TournamentId = tournamentId,
            EntryId = entryId,
            Type = nameof(TransactionType.Payment),
            Amount = request.Amount,
            Description = request.Notes ?? $"Pagamento - {entry.Person.FullName}",
            PaymentMethod = request.Method,
            PixAmount = request.PixAmount,
            CashAmount = request.CashAmount,
            PixDestinationId = request.PixDestinationCostExtraId,
            CreatedBy = createdBy
        };

        _db.Transactions.Add(transaction);

        // Atualizar PIX destination se informado
        if (request.PixDestinationCostExtraId.HasValue)
        {
            var costExtra = await _db.CostExtras
                .FirstOrDefaultAsync(c => c.Id == request.PixDestinationCostExtraId, ct);

            if (costExtra is not null && !costExtra.IsCashBox)
            {
                // Para Mixed usar pixAmount, para Pix puro usar amount
                var amountForCost = request.PixAmount ?? request.Amount;
                costExtra.PaidAmount += amountForCost;
                costExtra.PaymentStatus = costExtra.PaidAmount >= costExtra.Amount
                    ? nameof(Domain.Enums.PaymentStatus.Paid)
                    : nameof(Domain.Enums.PaymentStatus.PartiallyPaid);

                if (costExtra.PaymentStatus == nameof(Domain.Enums.PaymentStatus.Paid))
                {
                    costExtra.PaidAt = DateTimeOffset.UtcNow;
                    costExtra.PaymentMethod = "Pix";
                }
            }
        }

        // Recalcular saldo do entry
        entry.TotalPaid += request.Amount;
        entry.Balance = entry.TotalDue - entry.TotalPaid;

        entry.PaymentStatus = entry.Balance switch
        {
            0 => nameof(Domain.Enums.PaymentStatus.Paid),
            > 0 when entry.TotalPaid > 0 => nameof(Domain.Enums.PaymentStatus.PartiallyPaid),
            < 0 => nameof(Domain.Enums.PaymentStatus.Overpaid),
            _ => nameof(Domain.Enums.PaymentStatus.Pending)
        };

        await _db.SaveChangesAsync(ct);

        return new EntryResponse
        {
            Id = entry.Id,
            TournamentId = entry.TournamentId,
            PersonId = entry.PersonId,
            Status = entry.Status,
            BuyInPaid = entry.BuyInPaid,
            BuyInAmount = entry.BuyInAmount,
            RebuyCount = entry.RebuyCount,
            RebuyTotal = entry.RebuyTotal,
            AddonPurchased = entry.AddonPurchased,
            AddonAmount = entry.AddonAmount,
            TotalDue = entry.TotalDue,
            TotalPaid = entry.TotalPaid,
            Balance = entry.Balance,
            PaymentStatus = entry.PaymentStatus,
            Person = new PersonResponse
            {
                Id = entry.Person.Id,
                FullName = entry.Person.FullName,
                Nickname = entry.Person.Nickname,
                PhotoUrl = entry.Person.PhotoUrl
            }
        };
    }

    public async Task<PaymentSummaryResponse> GetPaymentSummaryAsync(
        Guid tournamentId, CancellationToken ct = default)
    {
        var entries = await _db.TournamentEntries
            .Include(e => e.Person)
            .Where(e => e.TournamentId == tournamentId)
            .OrderBy(e => e.Person.FullName)
            .ToListAsync(ct);

        var summary = new PaymentSummaryResponse
        {
            TotalDue = entries.Sum(e => e.TotalDue),
            TotalPaid = entries.Sum(e => e.TotalPaid),
            TotalPending = entries.Where(e => e.Balance > 0).Sum(e => e.Balance),
            PaidCount = entries.Count(e => e.PaymentStatus == nameof(Domain.Enums.PaymentStatus.Paid)),
            PendingCount = entries.Count(e => e.PaymentStatus != nameof(Domain.Enums.PaymentStatus.Paid)),
            Entries = entries.Select(e => new EntryPaymentInfo
            {
                EntryId = e.Id,
                PlayerName = e.Person.FullName,
                PhotoUrl = e.Person.PhotoUrl,
                TotalDue = e.TotalDue,
                TotalPaid = e.TotalPaid,
                Balance = e.Balance,
                PaymentStatus = e.PaymentStatus
            }).ToList()
        };

        return summary;
    }

    public async Task<List<PixSuggestionResponse>> GetPixSuggestionsAsync(
        Guid tournamentId, decimal playerDueAmount, CancellationToken ct = default)
    {
        var pendingCosts = await _db.CostExtras
            .Where(c => c.TournamentId == tournamentId
                     && c.PaymentStatus != nameof(Domain.Enums.PaymentStatus.Paid)
                     && !c.IsCashBox)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var suggestions = new List<PixSuggestionResponse>();

        foreach (var cost in pendingCosts)
        {
            var pendingAmount = cost.Amount - cost.PaidAmount;
            var matchScore = 1.0m - Math.Abs(pendingAmount - playerDueAmount)
                             / Math.Max(pendingAmount, playerDueAmount);

            suggestions.Add(new PixSuggestionResponse
            {
                CostExtraId = cost.Id,
                Description = cost.Description,
                PendingAmount = pendingAmount,
                PixKey = cost.PixKey,
                Beneficiary = cost.Beneficiary,
                MatchScore = Math.Max(0, matchScore),
                SuggestedPixAmount = Math.Min(pendingAmount, playerDueAmount)
            });
        }

        suggestions = suggestions.OrderByDescending(s => s.MatchScore).ToList();

        // Incluir caixa como última opção
        var cashBox = await _db.CostExtras
            .FirstOrDefaultAsync(c => c.TournamentId == tournamentId && c.IsCashBox, ct);

        if (cashBox is not null)
        {
            suggestions.Add(new PixSuggestionResponse
            {
                CostExtraId = cashBox.Id,
                Description = "Caixa",
                PendingAmount = 0,
                PixKey = cashBox.PixKey,
                Beneficiary = cashBox.Beneficiary,
                MatchScore = 0,
                SuggestedPixAmount = playerDueAmount - suggestions.Sum(s => s.SuggestedPixAmount)
            });
        }

        return suggestions;
    }
}
