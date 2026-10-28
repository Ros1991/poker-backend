using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

public class PaymentService
{
    // Meio centavo: tolerância das comparações de dinheiro (valores gravados com 2 casas)
    private const decimal HalfCent = 0.005m;

    // Valores em pt-BR sem depender de ICU/cultura do servidor: R$ 1.234,56 (mesmo formato do TournamentHistoryService)
    private static readonly NumberFormatInfo PtBrNumber = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberDecimalDigits = 2
    };

    private readonly IAppDbContext _db;

    public PaymentService(IAppDbContext db)
    {
        _db = db;
    }

    private static string Money(decimal value) => $"R$ {value.ToString("N2", PtBrNumber)}";

    /// <summary>
    /// Recusa do pagamento acima do que o destino ainda tem a receber (SPEC-20261026-1906). A tela mostra o mesmo
    /// "&lt;destino&gt; só recebe mais R$ X" e oferece lançar o restante no caixa ou em outro destino.
    /// </summary>
    public static string DestinationLimitMessage(string description, decimal remaining) =>
        remaining <= HalfCent
            ? $"{description} já recebeu tudo o que tinha a receber. Lance no caixa ou em outro destino."
            : $"{description} só recebe mais {Money(remaining)}. Lance o restante no caixa ou em outro destino.";

    private static bool IsMixedMethod(string? method) => method is "Mixed" or "Misto";

    public async Task<EntryResponse> RegisterPaymentAsync(
        Guid tournamentId, Guid entryId, CreatePaymentRequest request, Guid? createdBy = null, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var entry = await _db.EntriesWithPerson()
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        if (request.Amount <= 0)
            throw new DomainException("Valor do pagamento deve ser positivo.");

        // Partes do pagamento coerentes com o total: o detalhamento por forma e por destino soma exatamente o pago
        if (request.PixAmount < 0 || request.CashAmount < 0)
            throw new DomainException("Os valores de PIX e de dinheiro não podem ser negativos.");
        if (request.PixAmount > request.Amount + HalfCent)
            throw new DomainException("O valor do PIX não pode passar do valor do pagamento.");
        if (IsMixedMethod(request.Method) && (request.PixAmount.HasValue || request.CashAmount.HasValue)
            && Math.Abs((request.PixAmount ?? 0m) + (request.CashAmount ?? 0m) - request.Amount) > 0.01m)
            throw new DomainException("No pagamento misto, PIX + dinheiro tem que dar o valor total.");

        // Destino do PIX tem que ser um custo deste torneio (o dealer só enxerga o torneio dele)
        CostExtra? pixDestination = null;
        if (request.PixDestinationCostExtraId.HasValue)
        {
            pixDestination = await _db.CostExtras
                .FirstOrDefaultAsync(c => c.Id == request.PixDestinationCostExtraId
                                       && c.TournamentId == tournamentId, ct)
                ?? throw new DomainException("Destino do PIX não pertence a este torneio.");
        }

        // Limite por destino (SPEC-20261026-1906): um destino que não é o caixa nunca recebe mais do que ainda tem a
        // receber — senão o excedente "some" das contas (nem no custo, nem no caixa). A tela limita e oferece lançar o
        // restante no caixa; aqui o servidor recusa (400) o que passar. O crédito no custo segue a regra de sempre:
        // PIX puro = o valor todo; misto = só a parte PIX (o dinheiro fica no caixa). Concorrência: o xmin do custo
        // faz o segundo pagamento simultâneo cair em 409.
        if (pixDestination is { IsCashBox: false } limited)
        {
            // Pago nunca abaixo do que os jogadores já pagaram a ele (dado antigo do "Reverter pagamento" que zerava)
            CostCredits.FitPaid(limited, await CostCredits.OfCostAsync(_db, limited, ct));
            var credit = request.PixAmount ?? request.Amount;
            var remaining = limited.Amount - limited.PaidAmount;
            if (credit > remaining + HalfCent)
                throw new DomainException(DestinationLimitMessage(limited.Description, Math.Max(0m, remaining)));
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
            AddonCount = entry.AddonCount,
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
        var entry = await _db.EntriesWithPerson()
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        var cost = await _db.CostExtras
            .FirstOrDefaultAsync(c => c.Id == costExtraId && c.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Custo não encontrado.");

        if (amount <= 0)
            throw new DomainException("Valor do abatimento deve ser positivo.");

        // Pago nunca abaixo do que os jogadores já pagaram a ele (revisão geral 2), antes do limite
        CostCredits.FitPaid(cost, await CostCredits.OfCostAsync(_db, cost, ct));

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
            // Custo que tinha encolhido abaixo do que os jogadores pagaram (pago limitado ao valor): o estorno de UM dos
            // pagamentos não pode deixá-lo abaixo do que os outros ainda cobrem (revisão geral 2)
            if (cost is not null)
                CostCredits.FitPaid(cost, await CostCredits.OfCostAsync(_db, cost, ct));
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
        AddonCount = entry.AddonCount,
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
        var entries = await _db.EntriesWithPerson()
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

    /// <summary>
    /// Destinos do PIX na tela de registrar pagamento (SPEC-20261026-1906): o Caixa (custo marcado como caixa, se
    /// houver) PRIMEIRO; depois os custos que ainda têm a receber, na ordem de sempre (mais parecido com o que o
    /// jogador deve; empate = ordem da aba Custos). Custo quitado ou sem nada a receber não aparece — não pode
    /// receber mais nada. A tela também põe o "Caixa (gestor financeiro)" (sem custo) primeiro.
    /// </summary>
    public async Task<List<PixSuggestionResponse>> GetPixSuggestionsAsync(
        Guid tournamentId, decimal playerDueAmount, CancellationToken ct = default)
    {
        // Pago e status pela regra única (jogadores + caixa, limitado ao valor — revisão geral 2), também para dado antigo
        // que ainda não passou por um recálculo: o que a tela oferece é o que o servidor aceita
        var costs = await _db.CostExtras
            .AsNoTracking()
            .Where(c => c.TournamentId == tournamentId && !c.IsCashBox)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Description)
            .ToListAsync(ct);
        var credits = await CostCredits.ByCostAsync(_db, tournamentId, costs, ct);

        var suggestions = new List<PixSuggestionResponse>();

        foreach (var cost in costs)
        {
            var (paid, status) = CostCredits.Fitted(cost, credits.GetValueOrDefault(cost.Id));
            if (status == nameof(Domain.Enums.PaymentStatus.Paid)) continue;
            var pendingAmount = cost.Amount - paid;
            if (pendingAmount <= HalfCent) continue;

            // Sem valor do jogador (a tela não manda) o denominador ainda é positivo: pendingAmount > 0
            var denominator = Math.Max(pendingAmount, playerDueAmount);
            var matchScore = denominator > 0
                ? 1.0m - Math.Abs(pendingAmount - playerDueAmount) / denominator
                : 0m;

            suggestions.Add(new PixSuggestionResponse
            {
                CostExtraId = cost.Id,
                Description = cost.Description,
                PendingAmount = pendingAmount,
                PixKey = cost.PixKey,
                Beneficiary = cost.Beneficiary,
                MatchScore = Math.Max(0, matchScore),
                SuggestedPixAmount = Math.Max(0m, Math.Min(pendingAmount, playerDueAmount))
            });
        }

        // OrderByDescending é estável: no empate fica a ordem da aba Custos
        suggestions = suggestions.OrderByDescending(s => s.MatchScore).ToList();

        // Caixa em primeiro (antes era a última opção)
        var cashBox = await _db.CostExtras
            .FirstOrDefaultAsync(c => c.TournamentId == tournamentId && c.IsCashBox, ct);

        if (cashBox is not null)
        {
            suggestions.Insert(0, new PixSuggestionResponse
            {
                CostExtraId = cashBox.Id,
                Description = "Caixa",
                PendingAmount = 0,
                PixKey = cashBox.PixKey,
                Beneficiary = cashBox.Beneficiary,
                MatchScore = 0,
                SuggestedPixAmount = Math.Max(0m, playerDueAmount - suggestions.Sum(s => s.SuggestedPixAmount)),
                IsCashBox = true
            });
        }

        return suggestions;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Detalhamento financeiro (SPEC-20261026-1906): por forma de pagamento e quem pagou para quem
    // ------------------------------------------------------------------------------------------------------------

    private const string KindCashBox = "CashBox";
    private const string KindCost = "Cost";
    private const string KindRemoved = "Removed";

    private const string MethodPix = "Pix";
    private const string MethodCash = "Dinheiro";
    private const string MethodMixed = "Misto";
    private const string MethodTransfer = "Transferencia";
    private const string MethodSettlement = "Abatimento";
    private const string MethodOther = "Outros";

    /// <summary>Forma gravada no lançamento → forma do detalhamento ("Cash" e "Mixed" são nomes antigos/do modal)</summary>
    private static string NormalizeMethod(string? method) => method switch
    {
        "Pix" or "PIX" or "pix" => MethodPix,
        "Dinheiro" or "Cash" or "dinheiro" => MethodCash,
        "Mixed" or "Misto" => MethodMixed,
        "Transferencia" or "Transferência" => MethodTransfer,
        _ => MethodOther
    };

    private static string MethodTotalLabel(string method) => method switch
    {
        MethodPix => "PIX",
        MethodCash => "Dinheiro",
        MethodTransfer => "Transferência",
        MethodSettlement => "Abatimento",
        _ => "Outras formas"
    };

    private static string LineMethodLabel(string method) => method switch
    {
        MethodPix => "PIX",
        MethodCash => "Dinheiro",
        MethodMixed => "PIX + dinheiro",
        MethodTransfer => "Transferência",
        MethodSettlement => "Abatimento",
        _ => "Outra forma"
    };

    private sealed record PayerInfo(string PlayerName, string FullName, string? PhotoUrl);

    /// <summary>
    /// Por forma de pagamento (PIX, dinheiro, transferência, abatimento e outras) e por destino (caixa, cada custo e
    /// cada abatimento), com a lista de quem pagou (jogador, valor, forma, hora). Mesma regra de crédito do registro:
    /// abatimento → o valor todo no custo; pagamento com destino custo → PixAmount ?? Amount no custo e o resto (o
    /// dinheiro do misto) no caixa; sem destino ou destino Caixa → tudo no caixa. Estornados não entram.
    /// </summary>
    public async Task<PaymentBreakdownResponse> GetPaymentBreakdownAsync(Guid tournamentId, CancellationToken ct = default)
        => (await BuildBreakdownAsync(tournamentId, ct)).Breakdown;

    /// <summary>Cards da aba Pagamentos, pela mesma conta do detalhamento (o misto credita o custo só com a parte PIX)</summary>
    public async Task<PaymentTotalsByMethodResponse> GetTotalsByMethodAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var (b, totalDue) = await BuildBreakdownAsync(tournamentId, ct);
        return new PaymentTotalsByMethodResponse
        {
            TotalPix = b.ByMethod.Where(m => m.Method == MethodPix).Sum(m => m.Amount),
            TotalCash = b.ByMethod.Where(m => m.Method == MethodCash).Sum(m => m.Amount),
            // Inclui abatimentos: não entram em PIX/Dinheiro, mas creditam um custo
            TotalToCosts = b.Destinations.Where(d => d.Kind != KindCashBox).Sum(d => d.ReceivedFromPlayers),
            TotalPending = totalDue - b.TotalPaidByEntries
        };
    }

    private async Task<(PaymentBreakdownResponse Breakdown, decimal TotalDue)> BuildBreakdownAsync(
        Guid tournamentId, CancellationToken ct)
    {
        var transactions = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.TournamentId == tournamentId
                     && (t.Type == nameof(TransactionType.Payment)
                         || t.Type == nameof(TransactionType.Settlement))
                     && t.ReversedAt == null)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

        // Pessoa removida do cadastro continua com o nome (EntriesWithPerson ignora o filtro de soft delete)
        var entries = await _db.EntriesWithPerson()
            .AsNoTracking()
            .Where(e => e.TournamentId == tournamentId)
            .Select(e => new
            {
                e.Id,
                e.TotalDue,
                e.TotalPaid,
                e.Person.FullName,
                e.Person.Nickname,
                e.Person.PhotoUrl
            })
            .ToListAsync(ct);
        var payers = entries.ToDictionary(
            e => e.Id,
            e => new PayerInfo(
                string.IsNullOrWhiteSpace(e.Nickname) ? e.FullName : e.Nickname!,
                e.FullName,
                e.PhotoUrl));

        // Mesma ordem da aba Custos
        var costs = await _db.CostExtras
            .AsNoTracking()
            .Where(c => c.TournamentId == tournamentId)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Description)
            .ToListAsync(ct);

        var cashBox = new PaymentDestinationDetail
        {
            Kind = KindCashBox,
            Description = "Caixa (gestor financeiro)"
        };
        var costDetails = costs
            .Where(c => !c.IsCashBox)
            .Select(c => new PaymentDestinationDetail
            {
                Kind = KindCost,
                CostExtraId = c.Id,
                Description = c.Description,
                Beneficiary = c.Beneficiary,
                CostType = c.CostType,
                Amount = c.Amount,
                PaidAmount = c.PaidAmount,
                Remaining = Math.Max(0m, c.Amount - c.PaidAmount),
                PaymentStatus = c.PaymentStatus
            })
            .ToList();
        var costById = costDetails.ToDictionary(d => d.CostExtraId!.Value);
        var costEntityById = costs.Where(c => !c.IsCashBox).ToDictionary(c => c.Id);
        // Custo excluído (o lançamento perdeu o destino: FK ON DELETE SET NULL). Desde a revisão geral 2 não se exclui custo
        // com pagamento/abatimento valendo; os dados antigos são agrupados pelo nome que ficou na descrição do lançamento
        // ("Destino PIX: <nome>" do PaymentModal; "Abatimento de <jogador> no custo <nome>"), senão "Custo excluído".
        var removedByName = new Dictionary<string, PaymentDestinationDetail>(StringComparer.OrdinalIgnoreCase);
        var cashBoxNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caixa", cashBox.Description };
        foreach (var c in costs.Where(c => c.IsCashBox))
            cashBoxNames.Add(c.Description.Trim());
        PaymentDestinationDetail Removed(string? name)
        {
            var key = name?.Trim() ?? string.Empty;
            if (!removedByName.TryGetValue(key, out var d))
                removedByName[key] = d = new PaymentDestinationDetail
                {
                    Kind = KindRemoved,
                    Description = key.Length == 0 ? "Custo excluído" : $"Custo excluído ({key})"
                };
            return d;
        }
        const string PixNotePrefix = "Destino PIX: ";

        var methods = new Dictionary<string, PaymentMethodTotal>();
        void AddMethod(string method, decimal amount)
        {
            if (amount <= 0) return;
            if (!methods.TryGetValue(method, out var total))
                methods[method] = total = new PaymentMethodTotal { Method = method, Label = MethodTotalLabel(method) };
            total.Amount += amount;
            total.Count++;
        }

        foreach (var t in transactions)
        {
            var payer = t.EntryId is { } entryId && payers.TryGetValue(entryId, out var p) ? p : null;
            PaymentDestinationLine Line(decimal amount, string method) => new()
            {
                TransactionId = t.Id,
                EntryId = t.EntryId,
                PlayerName = payer?.PlayerName ?? "Jogador removido",
                FullName = payer?.FullName,
                PhotoUrl = payer?.PhotoUrl,
                Amount = amount,
                Method = method,
                MethodLabel = LineMethodLabel(method),
                IsSettlement = t.Type == nameof(TransactionType.Settlement),
                CreatedAt = t.CreatedAt
            };

            if (t.Type == nameof(TransactionType.Settlement))
            {
                // Abatimento: sem dinheiro trocar de mãos, o valor todo é do custo abatido
                AddMethod(MethodSettlement, t.Amount);
                var settledPrefix = payer is null ? null : $"Abatimento de {payer.FullName} no custo ";
                var target = t.PixDestinationId is { } settled && costById.TryGetValue(settled, out var sd)
                    ? sd
                    : Removed(settledPrefix is not null && t.Description?.StartsWith(settledPrefix, StringComparison.Ordinal) == true
                        ? t.Description[settledPrefix.Length..]
                        : null);
                target.Lines.Add(Line(t.Amount, MethodSettlement));
                target.SettledAmount += t.Amount;
                continue;
            }

            var method = NormalizeMethod(t.PaymentMethod);
            if (method == MethodMixed)
            {
                // A parte PIX nunca passa do total; o resto é o dinheiro (o total sempre bate)
                var pixPart = Math.Clamp(t.PixAmount ?? 0m, 0m, t.Amount);
                AddMethod(MethodPix, pixPart);
                AddMethod(MethodCash, t.Amount - pixPart);
            }
            else
            {
                AddMethod(method, t.Amount);
            }

            // Destino custo (que não é o Caixa): PixAmount ?? Amount no custo; o resto (dinheiro do misto) no caixa.
            // PIX sem destino com "Destino PIX: <nome>" de um custo (não o caixa) = pago a um custo que foi excluído.
            var deletedName = t.PixDestinationId is null && method != MethodCash
                && t.Description?.StartsWith(PixNotePrefix, StringComparison.Ordinal) == true
                    ? t.Description[PixNotePrefix.Length..].Trim()
                    : null;
            PaymentDestinationDetail? costTarget =
                t.PixDestinationId is { } dest && costById.TryGetValue(dest, out var cost) ? cost
                : !string.IsNullOrEmpty(deletedName) && !cashBoxNames.Contains(deletedName) ? Removed(deletedName)
                : null;
            if (costTarget is not null)
            {
                var credited = Math.Clamp(t.PixAmount ?? t.Amount, 0m, t.Amount);
                if (credited > 0)
                    costTarget.Lines.Add(Line(credited, method == MethodMixed ? MethodPix : method));
                var rest = t.Amount - credited;
                if (rest > 0)
                    cashBox.Lines.Add(Line(rest, method == MethodMixed ? MethodCash : method));
            }
            else
            {
                cashBox.Lines.Add(Line(t.Amount, method));
            }
        }

        var destinations = new List<PaymentDestinationDetail> { cashBox };
        destinations.AddRange(costDetails);
        destinations.AddRange(removedByName.Values);
        foreach (var d in destinations)
        {
            d.ReceivedFromPlayers = d.Lines.Sum(l => l.Amount);
            if (d.Kind == KindCost && d.CostExtraId is { } costId && costEntityById.TryGetValue(costId, out var entity))
            {
                // Pela regra única (revisão geral 2): pago = mín(valor, jogadores + caixa); a parte do caixa é a guardada no
                // custo. Custo que encolheu depois de pago (por jogadores e/ou pelo caixa): o excedente fica fora do devido
                var (paid, status) = CostCredits.Fitted(entity, d.ReceivedFromPlayers);
                d.PaidAmount = paid;
                d.Remaining = Math.Max(0m, entity.Amount - paid);
                d.PaymentStatus = status;
                d.PaidByCashBox = Math.Max(0m, entity.PaidByCashBox);
                d.OverReceived = CostCredits.OverReceived(entity, d.ReceivedFromPlayers);
            }
        }

        // PIX e Dinheiro sempre; as demais só quando houver
        var byMethod = new List<PaymentMethodTotal>();
        foreach (var m in new[] { MethodPix, MethodCash, MethodTransfer, MethodSettlement, MethodOther })
        {
            if (methods.TryGetValue(m, out var total))
                byMethod.Add(total);
            else if (m is MethodPix or MethodCash)
                byMethod.Add(new PaymentMethodTotal { Method = m, Label = MethodTotalLabel(m) });
        }

        var breakdown = new PaymentBreakdownResponse
        {
            TotalReceived = transactions.Sum(t => t.Amount),
            TotalPaidByEntries = entries.Sum(e => e.TotalPaid),
            ByMethod = byMethod,
            Destinations = destinations
        };
        return (breakdown, entries.Sum(e => e.TotalDue));
    }
}
