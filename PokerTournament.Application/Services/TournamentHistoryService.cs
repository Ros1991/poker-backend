using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Histórico único do torneio para o terminal: transações (inscrição, rebuy, add-on,
/// pagamento, abatimento, prêmio), eliminações e atividades (mesas, movimentações),
/// do mais recente para o mais antigo, com quem fez e se dá para desfazer.
/// O desfazer delega para os fluxos de sempre (EntryService/PaymentService), que mantêm
/// contadores, saldo e custos automáticos consistentes.
/// </summary>
public class TournamentHistoryService
{
    private const string KindElimination = "Elimination";
    private const string KindActivity = "Activity";

    // Valores em pt-BR sem depender de ICU/cultura do servidor: R$ 1.234,56
    private static readonly NumberFormatInfo PtBrNumber = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberDecimalDigits = 2
    };

    private readonly IAppDbContext _db;
    private readonly EntryService _entryService;
    private readonly PaymentService _paymentService;

    public TournamentHistoryService(IAppDbContext db, EntryService entryService, PaymentService paymentService)
    {
        _db = db;
        _entryService = entryService;
        _paymentService = paymentService;
    }

    public async Task<List<HistoryItemResponse>> GetHistoryAsync(
        Guid tournamentId, Guid? entryId = null, CancellationToken ct = default)
    {
        var tournamentStatus = await _db.Tournaments
            .Where(t => t.Id == tournamentId)
            .Select(t => t.Status)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Torneio não encontrado.");

        // 1) inscrições do torneio (nomes de jogadores e eliminadores, status para o desfazer)
        var entries = await _db.TournamentEntries
            .AsNoTracking()
            .Include(e => e.Person)
            .Where(e => e.TournamentId == tournamentId)
            .ToListAsync(ct);
        var entryMap = entries.ToDictionary(e => e.Id);

        // 2) transações (+ custo de destino do PIX/abatimento)
        var transactions = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.PixDestination)
            .Where(t => t.TournamentId == tournamentId
                     && (entryId == null || t.EntryId == entryId)
                     && t.Type != nameof(TransactionType.Refund))
            .ToListAsync(ct);

        // 3) eliminações
        var eliminations = await _db.Eliminations
            .AsNoTracking()
            .Where(e => e.TournamentId == tournamentId && (entryId == null || e.EntryId == entryId))
            .ToListAsync(ct);

        // 4) atividades
        var activities = await _db.TournamentActivities
            .AsNoTracking()
            .Where(a => a.TournamentId == tournamentId
                     && (entryId == null ? !a.PlayerOnly : a.EntryId == entryId))
            .ToListAsync(ct);

        // 5/6) quem fez: pedidos de dealer deste torneio (com nome), acessos antigos de dealer
        // (antes da autorização pelo organizador: só "Dealer") e usuários citados
        var actorIds = transactions.SelectMany(t => new[] { t.CreatedBy, t.ReversedBy })
            .Concat(eliminations.SelectMany(e => new[] { e.CreatedBy, e.CorrectedBy }))
            .Concat(activities.Select(a => a.CreatedBy))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var dealerNames = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _db.DealerRequests
                .AsNoTracking()
                .Where(r => r.TournamentId == tournamentId && actorIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Name })
                .ToListAsync(ct))
                .ToDictionary(r => r.Id, r => r.Name);

        var dealerIds = actorIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.DealerAccesses
                .AsNoTracking()
                .Where(a => a.TournamentId == tournamentId && actorIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync(ct)).ToHashSet();

        var userNames = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _db.Users
                .AsNoTracking()
                .Where(u => actorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.Nickname })
                .ToListAsync(ct))
                .ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.Nickname) ? u.FullName : u.Nickname!);

        string? ResolveActor(Guid? id)
        {
            if (!id.HasValue) return null;
            if (dealerNames.TryGetValue(id.Value, out var dealerName)) return $"Dealer · {dealerName}";
            if (dealerIds.Contains(id.Value)) return "Dealer";
            return userNames.TryGetValue(id.Value, out var name) ? name : null;
        }

        string? PlayerOf(Guid? id) =>
            id.HasValue && entryMap.TryGetValue(id.Value, out var e) ? EntryService.PlayerDisplayName(e.Person) : null;

        // Regras do desfazer inscrição: nada valendo (rebuy/add-on/pagamento/abatimento/eliminação).
        // Calculado sobre as transações/eliminações do próprio jogador (com ou sem filtro de entryId).
        var liveMoneyByEntry = transactions
            .Where(t => t.EntryId.HasValue && t.ReversedAt == null
                     && (t.Type == nameof(TransactionType.Rebuy)
                         || t.Type == nameof(TransactionType.Addon)
                         || t.Type == nameof(TransactionType.Payment)
                         || t.Type == nameof(TransactionType.Settlement)))
            .Select(t => t.EntryId!.Value)
            .ToHashSet();
        var liveEliminationByEntry = eliminations
            .Where(e => !e.Corrected)
            .Select(e => e.EntryId)
            .ToHashSet();

        // Prêmios pagos: bloqueiam o desfazer de eliminação (posições de quem já recebeu) e de
        // rebuy/add-on/inscrição (o bolo cairia abaixo do pago) — mesmas regras do EntryService.
        var paidPrizeEntryIds = await _entryService.PaidPrizeEntryIdsAsync(tournamentId, ct);
        var anyPrizePaid = paidPrizeEntryIds.Count > 0
            || entries.Any(e => EntryService.IsPrizePaid(e, paidPrizeEntryIds))
            || await _db.TournamentPrizes.AnyAsync(p => p.TournamentId == tournamentId && p.Paid, ct);
        var moneyUndoBlocked = EntryService.MoneyUndoBlockedReason(tournamentStatus, anyPrizePaid);

        var items = new List<HistoryItemResponse>(transactions.Count + eliminations.Count + activities.Count);

        foreach (var t in transactions)
        {
            var undone = t.ReversedAt != null;
            var item = new HistoryItemResponse
            {
                Id = $"{t.Type}:{t.Id}",
                Kind = t.Type,
                RefId = t.Id,
                EntryId = t.EntryId,
                PlayerName = PlayerOf(t.EntryId),
                Description = DescribeTransaction(t),
                Amount = t.Amount,
                At = t.CreatedAt,
                By = ResolveActor(t.CreatedBy),
                Undone = undone,
                UndoneAt = t.ReversedAt,
                UndoneBy = undone ? ResolveActor(t.ReversedBy) : null
            };

            switch (t.Type)
            {
                case nameof(TransactionType.Rebuy):
                case nameof(TransactionType.Addon):
                    item.CanUndo = !undone && t.EntryId.HasValue && moneyUndoBlocked is null;
                    if (!undone && moneyUndoBlocked is not null)
                        item.UndoBlockedReason = moneyUndoBlocked;
                    break;

                case nameof(TransactionType.Payment):
                case nameof(TransactionType.Settlement):
                    item.CanUndo = !undone && t.EntryId.HasValue;
                    break;

                case nameof(TransactionType.BuyIn):
                    if (!undone && t.EntryId.HasValue && moneyUndoBlocked is not null)
                    {
                        item.CanUndo = false;
                        item.UndoBlockedReason = moneyUndoBlocked;
                    }
                    else if (!undone && t.EntryId.HasValue)
                    {
                        var hasEntry = entryMap.TryGetValue(t.EntryId.Value, out var entry);
                        item.CanUndo = hasEntry
                            && (entry!.Status == nameof(EntryStatus.Active) || entry.Status == nameof(EntryStatus.Registered))
                            && entry.RebuyCount == 0
                            && !entry.AddonPurchased
                            && !liveMoneyByEntry.Contains(entry.Id)
                            && !liveEliminationByEntry.Contains(entry.Id);
                        if (!item.CanUndo)
                            item.UndoBlockedReason = EntryService.UndoRegistrationBlockedMessage;
                    }
                    break;

                default: // PrizePayment e outros: não se desfazem pelo terminal
                    item.CanUndo = false;
                    break;
            }

            items.Add(item);
        }

        foreach (var e in eliminations)
        {
            entryMap.TryGetValue(e.EntryId, out var entry);
            var item = new HistoryItemResponse
            {
                Id = $"{KindElimination}:{e.Id}",
                Kind = KindElimination,
                RefId = e.Id,
                EntryId = e.EntryId,
                PlayerName = PlayerOf(e.EntryId),
                Description = DescribeElimination(e, PlayerOf(e.EliminatedByEntryId)),
                Amount = null,
                At = e.EliminatedAt,
                By = ResolveActor(e.CreatedBy),
                Undone = e.Corrected,
                UndoneAt = e.CorrectedAt,
                UndoneBy = e.Corrected ? ResolveActor(e.CorrectedBy) : null
            };

            if (!e.Corrected)
            {
                // Prêmio pago dele ou de quem mudaria de posição (campeão e quem saiu depois)
                var paidBlocker = entry is null
                    ? null
                    : EntryService.FindPaidPrizeBlockingUndoElimination(entry, entries, paidPrizeEntryIds);
                if (paidBlocker is not null)
                {
                    item.CanUndo = false;
                    item.UndoBlockedReason = EntryService.UndoEliminationPrizePaidMessage(paidBlocker);
                }
                else
                {
                    item.CanUndo = entry is not null
                        && (entry.Status == nameof(EntryStatus.Eliminated) || entry.Status == nameof(EntryStatus.Awarded))
                        && entry.EliminatedAt != null;
                    if (!item.CanUndo)
                        item.UndoBlockedReason = "Esta eliminação não pode mais ser desfeita.";
                }
            }

            items.Add(item);
        }

        foreach (var a in activities)
        {
            items.Add(new HistoryItemResponse
            {
                Id = $"{KindActivity}:{a.Id}",
                Kind = KindActivity,
                ActivityType = a.Type,
                RefId = a.Id,
                EntryId = a.EntryId,
                PlayerName = PlayerOf(a.EntryId),
                Description = a.Description,
                Amount = null,
                At = a.CreatedAt,
                By = ResolveActor(a.CreatedBy),
                CanUndo = false
            });
        }

        return items
            .OrderByDescending(i => i.At)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Desfaz uma transação do histórico: rebuy/add-on (marca como desfeito), pagamento/abatimento
    /// (estorno) ou inscrição (só sem nada valendo). Rebuy/add-on/inscrição ficam bloqueados com o
    /// torneio encerrado ou prêmio pago (EnsureMoneyUndoAllowedAsync). Prêmio e reembolso não se
    /// desfazem por aqui.
    /// </summary>
    public async Task<string> UndoTransactionAsync(
        Guid tournamentId, Guid transactionId, Guid? undoneBy = null, CancellationToken ct = default)
    {
        var tx = await _db.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId && t.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Item não encontrado no histórico deste torneio.");

        if (tx.ReversedAt != null)
            throw new DomainException("Este item já foi desfeito.");

        if (tx.Type == nameof(TransactionType.PrizePayment) || tx.Type == nameof(TransactionType.Refund))
            throw new DomainException("Este item não pode ser desfeito pelo terminal.");

        if (!tx.EntryId.HasValue)
            throw new DomainException("Inscrição não encontrada.");
        var entryId = tx.EntryId.Value;

        // Rebuy/add-on/inscrição mexem no bolo: bloqueados com torneio encerrado ou prêmio pago
        if (tx.Type is nameof(TransactionType.Rebuy) or nameof(TransactionType.Addon) or nameof(TransactionType.BuyIn))
            await _entryService.EnsureMoneyUndoAllowedAsync(tournamentId, ct);

        switch (tx.Type)
        {
            case nameof(TransactionType.Rebuy):
                await _entryService.UndoRebuyAsync(tournamentId, entryId, transactionId, undoneBy, ct);
                return "Rebuy desfeito.";

            case nameof(TransactionType.Addon):
                await _entryService.UndoAddonAsync(tournamentId, entryId, transactionId, undoneBy, ct);
                return "Add-on desfeito.";

            case nameof(TransactionType.Payment):
                await _paymentService.ReversePaymentAsync(tournamentId, entryId, transactionId, undoneBy, "Desfeito", ct);
                return "Pagamento desfeito.";

            case nameof(TransactionType.Settlement):
                await _paymentService.ReversePaymentAsync(tournamentId, entryId, transactionId, undoneBy, "Desfeito", ct);
                return "Abatimento desfeito.";

            case nameof(TransactionType.BuyIn):
                await _entryService.UndoRegistrationAsync(tournamentId, entryId, undoneBy, ct);
                return "Inscrição desfeita.";

            default:
                throw new DomainException("Este item não pode ser desfeito pelo terminal.");
        }
    }

    public async Task<string> UndoEliminationAsync(
        Guid tournamentId, Guid eliminationId, Guid? undoneBy = null, CancellationToken ct = default)
    {
        var elimination = await _db.Eliminations
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eliminationId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Eliminação não encontrada.");

        if (elimination.Corrected)
            throw new DomainException("Este item já foi desfeito.");

        await _entryService.UndoEliminationAsync(tournamentId, elimination.EntryId, undoneBy, ct);
        return "Eliminação desfeita.";
    }

    // ---------- textos ----------

    private static string Money(decimal value) => $"R$ {value.ToString("N2", PtBrNumber)}";

    private static string DescribeTransaction(Transaction t)
    {
        switch (t.Type)
        {
            case nameof(TransactionType.BuyIn):
                return $"Inscrição (buy-in {Money(t.Amount)})";

            case nameof(TransactionType.Rebuy):
                return $"Rebuy ({Money(t.Amount)})";

            case nameof(TransactionType.Addon):
                // RegisterAddonAsync grava "Addon Duplo - {nome}" quando é duplo
                var isDouble = t.Description?.StartsWith("Addon Duplo", StringComparison.OrdinalIgnoreCase) == true;
                return isDouble ? $"Add-on duplo ({Money(t.Amount)})" : $"Add-on ({Money(t.Amount)})";

            case nameof(TransactionType.Payment):
                var text = DescribePayment(t);
                if (t.PixDestination is { } dest)
                    text += $" → {dest.Description}";
                return text;

            case nameof(TransactionType.Settlement):
                return t.PixDestination is { } cost
                    ? $"Abatimento de {Money(t.Amount)} no custo {cost.Description}"
                    : $"Abatimento de {Money(t.Amount)}";

            case nameof(TransactionType.PrizePayment):
                return $"Prêmio pago: {Money(t.Amount)}";

            default:
                return t.Description ?? $"{t.Type}: {Money(t.Amount)}";
        }
    }

    private static string DescribePayment(Transaction t)
    {
        var pix = t.PixAmount ?? 0m;
        var cash = t.CashAmount ?? 0m;
        var method = t.PaymentMethod ?? string.Empty;

        // "Mixed" é o que o PaymentModal envia para PIX + dinheiro ("Misto" na tela)
        var isMixed = method is "Mixed" or "Misto" || (pix > 0 && cash > 0);
        if (isMixed)
        {
            if (pix > 0 && cash > 0)
                return $"Pagamento misto: {Money(pix)} PIX + {Money(cash)} dinheiro";
            if (pix > 0)
                return $"Pagamento via PIX: {Money(t.Amount)}";
            if (cash > 0)
                return $"Pagamento via dinheiro: {Money(t.Amount)}";
            return $"Pagamento misto: {Money(t.Amount)}";
        }

        return method switch
        {
            "Pix" => $"Pagamento via PIX: {Money(t.Amount)}",
            "Dinheiro" or "Cash" => $"Pagamento via dinheiro: {Money(t.Amount)}",
            "Transferencia" => $"Pagamento via transferência: {Money(t.Amount)}",
            _ => $"Pagamento: {Money(t.Amount)}"
        };
    }

    private static string DescribeElimination(Elimination e, string? eliminatorName)
    {
        // Texto neutro (serve para jogador e jogadora)
        var text = e.Position.HasValue ? $"Saiu em {e.Position}º lugar" : "Saiu do torneio";
        if (!string.IsNullOrWhiteSpace(eliminatorName))
            text += $" · quem eliminou: {eliminatorName}";
        return text;
    }
}
