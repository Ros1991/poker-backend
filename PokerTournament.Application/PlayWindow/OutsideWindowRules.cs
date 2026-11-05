using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.PlayWindow;

/// <summary>
/// Rebuy e add-on esquecidos (SPEC-20261105-1707) — o EntryService e o TournamentHistoryService usam daqui.
/// <list type="bullet">
/// <item>Fora do período o pedido não é mais recusado de vez: sem confirmação, 409 <c>outside_window</c>
/// (<see cref="OutsideWindowException"/>); com <c>confirmOutsideWindow: true</c>, lança e marca a transação
/// (<see cref="Transaction.OutsideWindow"/>), que o histórico mostra como "fora do período".</item>
/// <item>Rebuy fica fora do período depois do "rebuy até o nível N" (<see cref="Tournament.RebuyUntilLevel"/>, nível atual
/// acima dele) e no intervalo de acerto (status BreakSettlement, o "Encerrar Rebuys" do painel). Add-on não tem nível
/// limite no torneio: só o intervalo de acerto.</item>
/// <item>O late registration (<see cref="Tournament.LateRegistrationLevel"/>) só vale para inscrição nova; nunca bloqueou
/// rebuy nem add-on.</item>
/// <item>Continuam recusados como antes (400): torneio finalizado ou cancelado, rebuy/add-on desligado, jogador que não
/// está ativo e o máximo de rebuys/add-ons. Essas checagens vêm ANTES da pergunta: não se pergunta o que vai ser
/// recusado de qualquer jeito.</item>
/// </list>
/// </summary>
public static class OutsideWindowRules
{
    /// <summary>Sufixo da descrição no histórico ("Rebuy (R$ 50,00) · fora do período")</summary>
    public const string HistorySuffix = " · fora do período";

    /// <summary>Status em que rebuy e add-on são aceitos (com ou sem pergunta): rascunho a intervalo de acerto</summary>
    public static bool AcceptsLateActions(string status) =>
        status == nameof(TournamentStatus.Draft)
        || status == nameof(TournamentStatus.OpenForRegistration)
        || status == nameof(TournamentStatus.InProgress)
        || status == nameof(TournamentStatus.BreakSettlement);

    /// <summary>Rebuy fora do período? null = dentro (lança sem perguntar)</summary>
    public static OutsideWindowException? ForRebuy(Tournament t)
    {
        var levelPassed = t.RebuyUntilLevel.HasValue && t.CurrentLevel.HasValue && t.CurrentLevel > t.RebuyUntilLevel;
        if (levelPassed)
            return new OutsideWindowException(
                $"O período de rebuy acabou no nível {t.RebuyUntilLevel}.",
                nameof(TransactionType.Rebuy), OutsideWindowException.ReasonRebuyLevel, t.RebuyUntilLevel);
        if (t.Status == nameof(TournamentStatus.BreakSettlement))
            return new OutsideWindowException(
                "Os rebuys já foram encerrados (intervalo de acerto).",
                nameof(TransactionType.Rebuy), OutsideWindowException.ReasonSettlement, null);
        return null;
    }

    /// <summary>Add-on fora do período? null = dentro (lança sem perguntar)</summary>
    public static OutsideWindowException? ForAddon(Tournament t) =>
        t.Status == nameof(TournamentStatus.BreakSettlement)
            ? new OutsideWindowException(
                "Os add-ons já foram encerrados (intervalo de acerto).",
                nameof(TransactionType.Addon), OutsideWindowException.ReasonSettlement, null)
            : null;

    /// <summary>
    /// Confere o período: dentro, false; fora sem confirmação, lança o 409; fora com confirmação, true (o lançamento sai
    /// marcado como fora do período).
    /// </summary>
    public static bool Check(OutsideWindowException? outside, bool confirmed)
    {
        if (outside is null)
            return false;
        if (!confirmed)
            throw outside;
        return true;
    }
}
