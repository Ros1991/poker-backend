using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Application.Services;

/// <summary>
/// O que os jogadores já pagaram direto a cada custo (revisão geral 2 do programa Ajustes pós-lançamento, SPEC-20261026-1906).
/// Mesma regra do registro, do estorno e do detalhamento: abatimento = Amount; pagamento = PixAmount ?? Amount (PIX para o
/// Caixa não conta); estornado não conta. Usado para:
/// <list type="bullet">
/// <item>"Reverter pagamento" do custo desfazer SÓ a parte paga pelo caixa (o que os jogadores pagaram continua);</item>
/// <item>custo que encolhe abaixo do que já recebeu (dos jogadores e/ou do caixa — a parte do caixa fica guardada à parte em
/// <c>PaidByCashBox</c>): <c>PaidAmount</c> fica limitado ao valor (status Pago), o excedente aparece no detalhamento como
/// "recebidos a mais — acertar com o caixa" e, se o custo voltar a crescer, o que já foi pago volta a contar;</item>
/// <item>recusar editar um custo para menos do que já recebeu e excluir custo com pagamento ou abatimento valendo.</item>
/// </list>
/// </summary>
public static class CostCredits
{
    /// <summary>Meio centavo: tolerância das comparações de dinheiro (valores gravados com 2 casas)</summary>
    public const decimal HalfCent = 0.005m;

    public const string UnpayOnlyPlayersMessage =
        "Este custo foi pago por jogadores. Para desfazer, estorne o pagamento pelo histórico.";

    public const string DeleteWithPaymentsMessage =
        "Este custo já tem pagamento ou abatimento registrado. Estorne antes de excluir.";

    public static string ReduceBelowCreditsMessage(decimal credits) =>
        $"Este custo já recebeu {Money(credits)} de jogadores. Estorne antes de reduzir.";

    private static readonly System.Globalization.NumberFormatInfo PtBrNumber = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberDecimalDigits = 2
    };

    private static string Money(decimal value) => $"R$ {value.ToString("N2", PtBrNumber)}";

    /// <summary>Crédito de um lançamento (pagamento ou abatimento que vale) no custo de destino.</summary>
    public static decimal Credit(Transaction tx, CostExtra cost)
    {
        if (tx.ReversedAt != null) return 0m;
        if (tx.Type == nameof(TransactionType.Settlement)) return Math.Max(0m, tx.Amount);
        if (tx.Type != nameof(TransactionType.Payment) || cost.IsCashBox) return 0m;
        return Math.Clamp(tx.PixAmount ?? tx.Amount, 0m, Math.Max(0m, tx.Amount));
    }

    /// <summary>
    /// Créditos de jogadores por custo do torneio, com o estado DESTA requisição: carrega (e rastreia) os lançamentos
    /// com destino e lê o <c>Local</c> — entra o que foi adicionado, sai o que foi removido e o estorno feito agora
    /// (ReversedAt em memória) já não conta.
    /// </summary>
    public static async Task<Dictionary<Guid, decimal>> ByCostAsync(
        IAppDbContext db, Guid tournamentId, IEnumerable<CostExtra> costs, CancellationToken ct)
    {
        var payment = nameof(TransactionType.Payment);
        var settlement = nameof(TransactionType.Settlement);
        await db.Transactions
            .Where(t => t.TournamentId == tournamentId && t.PixDestinationId != null
                     && (t.Type == payment || t.Type == settlement))
            .LoadAsync(ct);

        var byId = costs.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var result = new Dictionary<Guid, decimal>();
        foreach (var t in db.Transactions.Local)
        {
            if (t.TournamentId != tournamentId || t.PixDestinationId is not { } dest) continue;
            if (!byId.TryGetValue(dest, out var cost)) continue;
            var credit = Credit(t, cost);
            if (credit <= 0m) continue;
            result[dest] = result.GetValueOrDefault(dest) + credit;
        }
        return result;
    }

    /// <summary>Créditos de jogadores de UM custo (mesma leitura de <see cref="ByCostAsync"/>).</summary>
    public static async Task<decimal> OfCostAsync(IAppDbContext db, CostExtra cost, CancellationToken ct) =>
        (await ByCostAsync(db, cost.TournamentId, [cost], ct)).GetValueOrDefault(cost.Id);

    /// <summary>
    /// Pago do custo pela regra única (revisão geral 2): mín(valor, créditos dos jogadores + parte do caixa), nunca negativo.
    /// A parte do caixa (<see cref="CostExtra.PaidByCashBox"/>) fica guardada à parte: custo que encolhe não a perde e,
    /// quando volta a crescer, ela volta a contar.
    /// </summary>
    public static decimal FittedPaid(CostExtra cost, decimal playerCredits) =>
        Math.Min(Math.Max(0m, cost.Amount), Received(cost, playerCredits));

    /// <summary>Tudo o que o custo recebeu: créditos dos jogadores + o que o caixa entregou</summary>
    public static decimal Received(CostExtra cost, decimal playerCredits) =>
        Math.Max(0m, playerCredits) + Math.Max(0m, cost.PaidByCashBox);

    /// <summary>
    /// Recebido ACIMA do valor (dos jogadores e/ou do caixa): custo que encolheu depois de pago. Fica fora do devido e o
    /// detalhamento mostra "R$ X recebidos a mais — acertar com o caixa".
    /// </summary>
    public static decimal OverReceived(CostExtra cost, decimal playerCredits) =>
        Math.Max(0m, Received(cost, playerCredits) - Math.Max(0m, cost.Amount));

    /// <summary>
    /// Pago e status do custo pela regra única, sem mexer nele (telas e detalhamento leem assim, inclusive dado antigo
    /// que ainda não passou por um recálculo). Custo de R$ 0 sem nada recebido mantém o status gravado.
    /// </summary>
    public static (decimal Paid, string Status) Fitted(CostExtra cost, decimal playerCredits)
    {
        var paid = FittedPaid(cost, playerCredits);
        if (cost.Amount <= HalfCent && paid <= HalfCent && Received(cost, playerCredits) <= HalfCent)
            return (paid, cost.PaymentStatus);
        var status = paid >= cost.Amount - HalfCent
            ? nameof(PaymentStatus.Paid)
            : paid > 0m ? nameof(PaymentStatus.PartiallyPaid) : nameof(PaymentStatus.Pending);
        return (paid, status);
    }

    /// <summary>
    /// Ajusta o pago e o status do custo ao valor de agora pela regra única (<see cref="Fitted"/>): nunca acima do valor
    /// (custo que encolheu fica Pago, com o excedente só no detalhamento), nunca abaixo do que os jogadores e o caixa
    /// pagaram e ainda cabe no valor (custo que voltou a crescer volta a ficar quitado). Valor que cresceu depois de
    /// quitado volta a Parcial e aparece de novo nos destinos do PIX. Sem mudança, nada muda. Não salva.
    /// </summary>
    public static void FitPaid(CostExtra cost, decimal playerCredits)
    {
        var (paid, status) = Fitted(cost, playerCredits);
        if (Math.Abs(paid - cost.PaidAmount) >= HalfCent)
            cost.PaidAmount = paid;
        if (cost.PaymentStatus == status) return;
        cost.PaymentStatus = status;
        if (status == nameof(PaymentStatus.Paid))
            cost.PaidAt ??= DateTimeOffset.UtcNow;
        else
            cost.PaidAt = null;
    }

    /// <summary>
    /// "Pagar" da aba Custos: o caixa entrega o que ainda falta (valor − pago) e o custo fica Pago. A parte do caixa
    /// cresce só com o que faltava — o que os jogadores já pagaram continua deles. Não salva.
    /// </summary>
    public static void PayRestFromCashBox(CostExtra cost, decimal playerCredits)
    {
        var missing = Math.Max(0m, cost.Amount) - FittedPaid(cost, playerCredits);
        if (missing >= HalfCent)
            cost.PaidByCashBox = Math.Max(0m, cost.PaidByCashBox) + missing;
        FitPaid(cost, playerCredits);
        cost.PaymentStatus = nameof(PaymentStatus.Paid);
        cost.PaidAt ??= DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// "Reverter pagamento": tira SÓ a parte do caixa; o que os jogadores pagaram continua valendo. Não salva.
    /// </summary>
    public static void UndoCashBox(CostExtra cost, decimal playerCredits)
    {
        cost.PaidByCashBox = 0m;
        FitPaid(cost, playerCredits);
    }

    /// <summary>Aplica <see cref="FitPaid"/> a todos os custos do torneio (depois de recalcular os valores). Não salva.</summary>
    public static async Task FitAllAsync(IAppDbContext db, Guid tournamentId, IReadOnlyCollection<CostExtra> costs, CancellationToken ct)
    {
        if (costs.Count == 0) return;
        var credits = await ByCostAsync(db, tournamentId, costs, ct);
        foreach (var c in costs)
            FitPaid(c, credits.GetValueOrDefault(c.Id));
    }
}
