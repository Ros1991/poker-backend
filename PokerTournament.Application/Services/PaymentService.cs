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

        // Destino do PIX tem que ser um custo deste torneio (o dealer só enxerga o torneio dele)
        CostExtra? pixDestination = null;
        if (request.PixDestinationCostExtraId.HasValue)
        {
            pixDestination = await _db.CostExtras
                .FirstOrDefaultAsync(c => c.Id == request.PixDestinationCostExtraId
                                       && c.TournamentId == tournamentId, ct)
                ?? throw new DomainException("Destino do PIX não pertence a este torneio.");
        }

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
        if (pixDestination is { } costExtra)
        {
            if (!costExtra.IsCashBox)
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

    /// <summary>
    /// Abatimento: o favorecido de um custo quita sua dívida contra esse custo.
    /// Duas pernas atômicas — quita o saldo do jogador E reduz o "a pagar" do custo.
    /// Ex.: dono deve 300, staff tem 1000 a receber → dono deve 0, staff a pagar = 700.
    /// </summary>
    public async Task<EntryResponse> SettleAgainstCostAsync(
        Guid tournamentId, Guid entryId, Guid costExtraId, decimal amount, Guid? createdBy = null, CancellationToken ct = default)
    {
        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        var cost = await _db.CostExtras
            .FirstOrDefaultAsync(c => c.Id == costExtraId && c.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Custo não encontrado.");

        if (amount <= 0)
            throw new DomainException("Valor do abatimento deve ser positivo.");

        // Triplo-Min: nunca abate além do que o jogador deve nem além do que o custo comporta.
        var settle = Math.Min(amount, Math.Min(entry.Balance, cost.Amount - cost.PaidAmount));
        if (settle <= 0)
            throw new DomainException("Nada a abater (jogador sem saldo devedor ou custo já quitado).");

        // Perna 1: quita a dívida do jogador
        entry.TotalPaid += settle;
        entry.Balance = entry.TotalDue - entry.TotalPaid;
        entry.PaymentStatus = RecalcPaymentStatus(entry.Balance, entry.TotalPaid);

        // Perna 2: credita o custo (reduz o "a pagar" = Amount - PaidAmount)
        cost.PaidAmount += settle;
        cost.PaymentStatus = cost.PaidAmount >= cost.Amount
            ? nameof(PaymentStatus.Paid)
            : nameof(PaymentStatus.PartiallyPaid);
        if (cost.PaymentStatus == nameof(PaymentStatus.Paid))
        {
            cost.PaidAt = DateTimeOffset.UtcNow;
            cost.PaymentMethod = "Compensacao";
        }

        // Trilha de auditoria (reversível pelo estorno, que já trata PixDestinationId)
        _db.Transactions.Add(new Transaction
        {
            TournamentId = tournamentId,
            EntryId = entryId,
            Type = nameof(TransactionType.Settlement),
            Amount = settle,
            Description = $"Abatimento de {entry.Person.FullName} no custo {cost.Description}",
            PaymentMethod = "Compensacao",
            PixDestinationId = costExtraId,
            CreatedBy = createdBy
        });

        await _db.SaveChangesAsync(ct);
        return MapEntry(entry);
    }

    /// <summary>
    /// Estorno de pagamento/abatimento: MARCA a transação como estornada (não apaga),
    /// devolve o valor ao saldo do jogador e desfaz o crédito no custo de destino
    /// exatamente como o registro o fez (PIX → PixAmount ?? Amount, nunca no Caixa;
    /// abatimento → Amount).
    /// </summary>
    public async Task ReversePaymentAsync(
        Guid tournamentId, Guid entryId, Guid transactionId, Guid? reversedBy = null,
        string reason = "Estornado pelo organizador", CancellationToken ct = default)
    {
        var tx = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == transactionId
                                   && t.TournamentId == tournamentId
                                   && t.EntryId == entryId
                                   && (t.Type == nameof(TransactionType.Payment)
                                       || t.Type == nameof(TransactionType.Settlement)), ct)
            ?? throw new DomainException("Pagamento não encontrado.");

        if (tx.ReversedAt != null)
            throw new DomainException("Pagamento já foi estornado.");

        // Marcar como estornado
        tx.ReversedAt = DateTimeOffset.UtcNow;
        tx.ReversedBy = reversedBy;
        tx.ReversalReason = reason;

        // Atualizar entry
        var entry = await _db.TournamentEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct);
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

        // Se creditou um custo extra, desfaz o crédito (mesmo valor que o registro somou)
        if (tx.PixDestinationId.HasValue)
        {
            var cost = await _db.CostExtras
                .FirstOrDefaultAsync(c => c.Id == tx.PixDestinationId && c.TournamentId == tournamentId, ct);
            ReverseCostCredit(tx, cost);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Desfaz no custo de destino o crédito que o pagamento/abatimento somou ao ser registrado:
    /// abatimento → Amount; pagamento → PixAmount ?? Amount, nunca no Caixa. Regra única do
    /// estorno e da exclusão da inscrição. Não salva.
    /// </summary>
    public static void ReverseCostCredit(Transaction tx, CostExtra? cost)
    {
        if (cost is null) return;

        decimal credited = 0m;
        if (tx.Type == nameof(TransactionType.Settlement))
            credited = tx.Amount;
        else if (!cost.IsCashBox)
            credited = tx.PixAmount ?? tx.Amount;

        if (credited <= 0) return;

        cost.PaidAmount = Math.Max(0, cost.PaidAmount - credited);
        if (cost.PaidAmount < cost.Amount)
        {
            cost.PaymentStatus = cost.PaidAmount > 0
                ? nameof(PaymentStatus.PartiallyPaid)
                : nameof(PaymentStatus.Pending);
            cost.PaidAt = null;
        }
    }

    private static string RecalcPaymentStatus(decimal balance, decimal totalPaid)
    {
        if (Math.Abs(balance) < 0.005m) return nameof(PaymentStatus.Paid);
        if (balance > 0 && totalPaid > 0) return nameof(PaymentStatus.PartiallyPaid);
        if (balance < 0) return nameof(PaymentStatus.Overpaid);
        return nameof(PaymentStatus.Pending);
    }

    private static EntryResponse MapEntry(TournamentEntry entry) => new()
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
