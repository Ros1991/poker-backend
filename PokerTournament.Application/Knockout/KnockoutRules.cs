using System.Globalization;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Knockout;

/// <summary>
/// Regras do torneio knockout (SPEC-20261026-1909) num lugar só — EntryService, TournamentService, PrizeService,
/// DisplayService, TournamentHistoryService e CostExtrasController usam daqui.
/// <list type="bullet">
/// <item>Formato: Normal (None), Knockout simples (Simple) ou progressivo (Progressive), gravado como string.</item>
/// <item>Bounty: valor fixo por entrada, PARTE do buy-in (o resto vai para o prize pool); cada rebuy compra um bounty
/// novo do mesmo valor, que entra na cabeça do jogador; add-on sem bounty.</item>
/// <item>Eliminação: no simples quem eliminou leva 100% da cabeça do eliminado; no PKO leva metade em dinheiro e a outra
/// metade soma na própria cabeça. Quem nunca foi eliminado (o campeão) fica com a própria cabeça no fim.</item>
/// <item>Dinheiro: bounties à parte do prize pool — líquido = arrecadado − custos − bounties.</item>
/// </list>
/// </summary>
public static class KnockoutRules
{
    public const string KillerRequiredMessage = "Em torneio knockout, escolha quem eliminou o jogador.";
    public const string KillerNotPlayingMessage = "Quem eliminou precisa ser um jogador que ainda está no torneio.";
    public const string InvalidTypeMessage = "Formato de torneio inválido. Use Normal, Knockout simples ou Knockout progressivo.";
    public const string BountyRequiredMessage = "Informe o valor do bounty do torneio knockout.";
    public const string BountyNotLessThanBuyInMessage =
        "O bounty é parte do buy-in: precisa ser menor que o buy-in (o resto vai para o prize pool).";
    public const string BountyAboveRebuyMessage =
        "Cada rebuy compra um bounty novo: o bounty não pode passar do valor do rebuy.";
    public const string ChangeWithEliminationsMessage =
        "Já há eliminação com bounty neste torneio. Desfaça as eliminações antes de mudar o formato ou o valor do bounty.";
    public const string UndoRebuyEliminatedMessage =
        "O bounty deste rebuy já foi para quem eliminou o jogador. Desfaça a eliminação antes.";
    public const string RemoveEntryWithBountyMessage =
        "Este jogador tem bounty ganho ou já foi eliminado no knockout. Desfaça antes as eliminações dele.";
    public const decimal MaxBountyAmount = 99_999.99m;

    // R$ 1.234,56 sem depender da cultura do servidor
    private static readonly NumberFormatInfo PtBrNumber = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberDecimalDigits = 2
    };

    public static string Money(decimal value) => $"R$ {value.ToString("N2", PtBrNumber)}";

    public static bool IsKnockout(string? type) =>
        type == nameof(KnockoutType.Simple) || type == nameof(KnockoutType.Progressive);

    public static bool IsKnockout(Tournament tournament) =>
        IsKnockout(tournament.KnockoutType) && tournament.BountyAmount is > 0;

    public static bool IsProgressive(Tournament tournament) =>
        tournament.KnockoutType == nameof(KnockoutType.Progressive) && tournament.BountyAmount is > 0;

    /// <summary>Bounty que cada inscrição/rebuy compra (0 no Normal).</summary>
    public static decimal BountyPerEntry(Tournament tournament) =>
        IsKnockout(tournament) ? tournament.BountyAmount!.Value : 0m;

    /// <summary>
    /// Tipo do pedido → nome gravado. Aceita os nomes do enum (qualquer caixa) e "Normal" como sinônimo de None;
    /// null/vazio devolve null (quem chama decide: manter, herdar da estrutura ou Normal). Outro texto → 400.
    /// </summary>
    public static string? Parse(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return null;
        var t = type.Trim();
        if (t.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            return nameof(KnockoutType.None);
        if (Enum.TryParse<KnockoutType>(t, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            && !int.TryParse(t, out _))
            return parsed.ToString();
        throw new DomainException(InvalidTypeMessage);
    }

    /// <summary>
    /// Formato + bounty finais (validados). Normal → (None, null). Knockout exige bounty &gt; 0, menor que o buy-in e,
    /// com rebuy habilitado, até o valor do rebuy. Valores com 2 casas.
    /// </summary>
    public static (string Type, decimal? Bounty) Resolve(string type, decimal? bounty, decimal buyIn, decimal? rebuy)
    {
        if (!IsKnockout(type))
            return (nameof(KnockoutType.None), null);
        if (bounty is not > 0 || bounty > MaxBountyAmount)
            throw new DomainException(BountyRequiredMessage);
        var value = Math.Round(bounty.Value, 2, MidpointRounding.AwayFromZero);
        if (value >= buyIn)
            throw new DomainException(BountyNotLessThanBuyInMessage);
        if (rebuy is > 0 && value > rebuy)
            throw new DomainException(BountyAboveRebuyMessage);
        return (type, value);
    }

    /// <summary>
    /// Padrão da estrutura: mesmo formato; o valor só é exigido (&gt; 0) no knockout. Com buy-in padrão informado, o bounty
    /// precisa ser menor que ele (revisão geral 2, a mesma recusa do torneio); sem buy-in padrão, o buy-in é conferido no
    /// torneio.
    /// </summary>
    public static (string Type, decimal? Bounty) ResolveStructureDefault(string type, decimal? bounty, decimal? defaultBuyIn = null)
    {
        if (!IsKnockout(type))
            return (nameof(KnockoutType.None), null);
        if (bounty is not > 0 || bounty > MaxBountyAmount)
            throw new DomainException(BountyRequiredMessage);
        var value = Math.Round(bounty.Value, 2, MidpointRounding.AwayFromZero);
        EnsureBelowDefaultBuyIn(type, value, defaultBuyIn);
        return (type, value);
    }

    /// <summary>Bounty padrão de knockout ≥ buy-in padrão (&gt; 0) → 400 com a mesma mensagem do torneio.</summary>
    public static void EnsureBelowDefaultBuyIn(string? type, decimal? bounty, decimal? defaultBuyIn)
    {
        if (IsKnockout(type) && bounty is > 0 && defaultBuyIn is > 0 && bounty >= defaultBuyIn)
            throw new DomainException(BountyNotLessThanBuyInMessage);
    }

    /// <summary>
    /// Divide a cabeça do eliminado: simples = tudo em dinheiro; PKO = metade em dinheiro (arredondada para baixo no
    /// centavo) e o resto na cabeça de quem eliminou. Sempre cash + toHead = head.
    /// </summary>
    public static (decimal Cash, decimal ToHead) Split(Tournament tournament, decimal head)
    {
        if (head <= 0)
            return (0m, 0m);
        if (!IsProgressive(tournament))
            return (head, 0m);
        var cash = Math.Floor(head * 50m) / 100m;
        return (cash, head - cash);
    }

    /// <summary>NetPrizePool = arrecadado − custos − bounties (bounty não é prize pool).</summary>
    public static void ApplyNet(Tournament tournament) =>
        tournament.NetPrizePool = tournament.TotalPrizePool - tournament.TotalCosts - tournament.TotalBounties;

    /// <summary>Líquido de uma arrecadação/custos lidos agora (Premiação e telão), sem os bounties.</summary>
    public static decimal Net(decimal gross, decimal costs, decimal bounties) => gross - costs - bounties;

    /// <summary>
    /// Refaz cabeças e o total de bounties a partir do zero (mudança de formato/valor num rascunho SEM eliminação com
    /// bounty): cabeça = bounty × (1 + rebuys) de cada inscrição ainda jogando, nada ganho; total = soma das compras.
    /// </summary>
    public static void RecomputeFromScratch(Tournament tournament, IEnumerable<TournamentEntry> entries)
    {
        var per = BountyPerEntry(tournament);
        decimal total = 0m;
        foreach (var e in entries)
        {
            var bought = per * (1 + e.RebuyCount);
            total += bought;
            e.BountyWon = 0m;
            e.BountyHead = bought;
        }
        tournament.TotalBounties = total;
    }

    /// <summary>
    /// Quem fica com a própria cabeça no acerto: quem nunca foi eliminado (o campeão; ou quem ainda jogava quando o
    /// torneio foi encerrado à mão). Eliminado (EliminatedAt preenchido) não tem cabeça — foi para quem eliminou.
    /// </summary>
    public static bool KeepsOwnHead(TournamentEntry entry) =>
        entry.EliminatedAt is null && entry.BountyHead > 0;

    /// <summary>Texto da eliminação no histórico: "· bounty R$ 50,00 para Ana" / PKO "· bounty R$ 50,00: R$ 25,00 para Ana + R$ 25,00 na cabeça".</summary>
    public static string? DescribeTransfer(Elimination e, string? eliminatorName)
    {
        if (e.BountyAmount is not > 0)
            return null;
        var who = string.IsNullOrWhiteSpace(eliminatorName) ? "quem eliminou" : eliminatorName;
        return e.BountyToHead is > 0
            ? $"bounty {Money(e.BountyAmount.Value)}: {Money(e.BountyCash ?? 0m)} para {who} + {Money(e.BountyToHead.Value)} na cabeça"
            : $"bounty {Money(e.BountyAmount.Value)} para {who}";
    }
}
