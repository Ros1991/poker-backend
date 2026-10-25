using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
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
    private readonly PlanService _plans;

    public TournamentHistoryService(
        IAppDbContext db, EntryService entryService, PaymentService paymentService, PlanService plans)
    {
        _db = db;
        _entryService = entryService;
        _paymentService = paymentService;
        _plans = plans;
    }

    public const string UndoWarningFinished =
        "O torneio já terminou. Desfazer muda o bolo depois do fim; confira a premiação.";

    public const string UndoWarningCancelled =
        "O torneio foi cancelado. Desfazer muda o bolo de um torneio cancelado.";

    public const string UndoWarningPrizePaid =
        "Já há prêmio pago. Desfazer diminui o bolo, mas o prêmio entregue não muda; confira a premiação.";

    /// <summary>
    /// Aviso do organizador (SPEC-20261023-1217, L4) nos casos em que o dealer fica bloqueado
    /// (<see cref="EntryService.MoneyUndoBlockedReason"/>): torneio encerrado, cancelado ou com prêmio pago. Null = sem aviso.
    /// </summary>
    public static string? MoneyUndoWarning(string tournamentStatus, bool anyPrizePaid) =>
        tournamentStatus == nameof(TournamentStatus.Finished) ? UndoWarningFinished
        : tournamentStatus == nameof(TournamentStatus.Cancelled) ? UndoWarningCancelled
        : anyPrizePaid ? UndoWarningPrizePaid
        : null;

    /// <summary>
    /// Histórico do torneio (ou de um jogador). <paramref name="asOrganizer"/> = token de usuário (dono, membro ou Admin
    /// que passou no TenantAccessFilter — SPEC-20261023-1217, L4): rebuy, add-on e inscrição NÃO ficam bloqueados pelo
    /// torneio encerrado/cancelado nem por prêmio pago; o item vem com <c>UndoWarning</c>. A regra "inscrição só sem nada
    /// valendo", a eliminação (prêmio pago dele ou de quem ficou à frente, campeão), o cadeado de plano do pagamento e o
    /// prêmio pago valem para todos. Sem <paramref name="asOrganizer"/> (token de dealer), exatamente como antes.
    /// </summary>
    public async Task<List<HistoryItemResponse>> GetHistoryAsync(
        Guid tournamentId, Guid? entryId = null, bool asOrganizer = false, CancellationToken ct = default)
    {
        var tournamentStatus = await _db.Tournaments
            .Where(t => t.Id == tournamentId)
            .Select(t => t.Status)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Torneio não encontrado.");

        // 1) inscrições do torneio (nomes de jogadores e eliminadores, status para o desfazer)
        // Com a pessoa removida do cadastro também (EntryQueries): o histórico continua com o nome dela
        var entries = await _db.TournamentEntries
            .IgnoreQueryFilters()
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
        // Organizador (token de usuário): o bloqueio do dealer vira aviso na confirmação (L4)
        var moneyUndoWarning = asOrganizer ? MoneyUndoWarning(tournamentStatus, anyPrizePaid) : null;
        if (asOrganizer)
            moneyUndoBlocked = null;
        // Estorno de pagamento/abatimento é financeiro: sem Payments no plano do dono (Mesa) não se desfaz por aqui
        // (o POST .../undo responde 402, como o estorno de payments/*)
        var paymentsUndoAllowed = await _plans.HasFeatureForTournamentAsync(tournamentId, PlanFeature.Payments, ct);

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
                    if (item.CanUndo)
                        item.UndoWarning = moneyUndoWarning;
                    break;

                case nameof(TransactionType.Payment):
                case nameof(TransactionType.Settlement):
                    item.CanUndo = !undone && t.EntryId.HasValue && paymentsUndoAllowed;
                    if (!undone && !paymentsUndoAllowed)
                        item.UndoBlockedReason = PlanCatalog.FeatureLockedMessage(PlanFeature.Payments);
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
                        else
                            item.UndoWarning = moneyUndoWarning;
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
                Position = e.Position,
                EliminatedByName = PlayerOf(e.EliminatedByEntryId),
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
    /// desfazem por aqui. Pagamento/abatimento é estorno (financeiro): exige o recurso Payments no plano do dono
    /// → 402 feature_locked, igual ao POST payments/entries/{id}/{paymentId}/reverse (o token de dealer nunca
    /// pula; o Admin do sistema passa <paramref name="bypassPlanLimits"/>). Rebuy/add-on/inscrição continuam sem
    /// checagem de plano (invariante da SPEC-20261023-1205). <paramref name="asOrganizer"/> (token de usuário —
    /// SPEC-20261023-1217, L4) pula o bloqueio de torneio encerrado/prêmio pago em rebuy/add-on/inscrição; a regra
    /// "inscrição só sem nada valendo" (UndoRegistrationAsync) continua valendo para todos.
    /// </summary>
    public async Task<string> UndoTransactionAsync(
        Guid tournamentId, Guid transactionId, Guid? undoneBy = null, bool bypassPlanLimits = false,
        bool asOrganizer = false, CancellationToken ct = default)
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

        // Rebuy/add-on/inscrição mexem no bolo: bloqueados com torneio encerrado ou prêmio pago — para o dealer; o
        // organizador desfaz mesmo assim (a tela mostrou o aviso na confirmação)
        if (!asOrganizer
            && tx.Type is nameof(TransactionType.Rebuy) or nameof(TransactionType.Addon) or nameof(TransactionType.BuyIn))
            await _entryService.EnsureMoneyUndoAllowedAsync(tournamentId, ct);

        // Estorno pelo histórico = estorno de payments/*: sem Payments no plano do dono → 402
        if (tx.Type is nameof(TransactionType.Payment) or nameof(TransactionType.Settlement))
            await _plans.EnsureFeatureForTournamentAsync(tournamentId, PlanFeature.Payments, bypassPlanLimits, ct: ct);

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

    /// <summary>
    /// Texto da atividade PlayerRemoved quando o organizador EXCLUI a inscrição (SPEC-20261023-1217, L5):
    /// "Inscrição excluída: Ana (buy-in, 2 rebuys e add-on · R$ 350,00 · pagou R$ 100,00)". O resumo vai na própria
    /// descrição porque a exclusão apaga de vez as transações do jogador (e, com elas, as linhas dele no histórico).
    /// Montar ANTES de apagar (usa os contadores da inscrição e as transações carregadas). Exige Person carregado.
    /// </summary>
    public static string DeletedEntryDescription(TournamentEntry entry)
    {
        var parts = new List<string> { "buy-in" };
        if (entry.RebuyCount > 0)
            parts.Add(entry.RebuyCount == 1 ? "1 rebuy" : $"{entry.RebuyCount} rebuys");
        if (entry.AddonPurchased)
            parts.Add(entry.AddonDouble ? "add-on duplo" : "add-on");
        var what = parts.Count == 1
            ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " e " + parts[^1];

        var paid = (entry.Transactions ?? new List<Transaction>())
            .Where(t => t.ReversedAt == null
                     && (t.Type == nameof(TransactionType.Payment) || t.Type == nameof(TransactionType.Settlement)))
            .Sum(t => t.Amount);

        var summary = $"{what} · {Money(entry.TotalDue)}";
        if (paid > 0)
            summary += $" · pagou {Money(paid)}";
        return $"Inscrição excluída: {EntryService.PlayerDisplayName(entry.Person)} ({summary})";
    }

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
