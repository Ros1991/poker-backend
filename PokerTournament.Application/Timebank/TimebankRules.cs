using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Timebank;

/// <summary>
/// Regras do timebank virtual (SPEC-20261029-1722) num lugar só — TournamentService, BlindStructuresController, EntryService
/// e TournamentHistoryService usam daqui.
/// <list type="bullet">
/// <item>Opcional por torneio: <see cref="Tournament.TimebankCount"/> null = sem timebank (nenhuma tela muda). Ligado, de
/// 1 a <see cref="MaxCount"/> por jogador, de <see cref="MinSeconds"/> a <see cref="MaxSeconds"/> segundos cada
/// (padrão <see cref="DefaultSeconds"/>). O mesmo vale para o padrão da estrutura de blinds.</item>
/// <item>Nos pedidos (torneio e estrutura), a quantidade 0 desliga; null = herdar da estrutura (criação) ou manter
/// (edição). O 0 nunca é gravado: desligado é null.</item>
/// <item>O celular do dealer conta o tempo (D4); o servidor só registra o uso e os ajustes, com desfazer pelo histórico.
/// O jogador nunca fica com menos de 0; o dealer pode dar mais que o inicial (até <see cref="MaxRemaining"/>).</item>
/// </list>
/// </summary>
public static class TimebankRules
{
    public const int MaxCount = 99;
    public const int MinSeconds = 5;
    public const int MaxSeconds = 300;
    public const int DefaultSeconds = 30;

    /// <summary>Teto de timebanks que um jogador pode acumular com os ajustes (+1) do dealer</summary>
    public const int MaxRemaining = 99;

    public const string InvalidCountMessage = "Timebanks por jogador inválido: use de 1 a 99 (0 desliga o timebank).";

    public const string InvalidSecondsMessage = "Segundos por timebank inválidos: use de 5 a 300.";

    public const string DisabledMessage = "Este torneio não usa timebank.";

    public const string NoneLeftMessage = "O jogador não tem mais timebank.";

    public const string InvalidDeltaMessage = "Ajuste de timebank inválido: use +1 ou −1.";

    public const string MaxRemainingMessage = "O jogador já tem o máximo de timebanks (99).";

    public const string PlayerNotActiveMessage = "Jogador não está ativo no torneio.";

    public const string PhaseMessage = "Não é possível usar timebank nesta fase do torneio.";

    public const string UndoNoneLeftMessage = "O jogador já usou esse timebank: não há timebank para retirar.";

    public static bool IsEnabled(Tournament tournament) => tournament.TimebankCount is > 0;

    /// <summary>
    /// Quantidade e segundos que valem a partir do pedido: <paramref name="requestedCount"/> null = <paramref name="fallbackCount"/>
    /// (herdar ou manter); 0 = desligado; ligado, 1 a 99. Segundos: o pedido, senão o <paramref name="fallbackSeconds"/>,
    /// senão 30; só valem com o timebank ligado (desligado = null nos dois). Fora dos limites → 400.
    /// </summary>
    public static (int? Count, int? Seconds) Resolve(int? requestedCount, int? requestedSeconds, int? fallbackCount, int? fallbackSeconds)
    {
        var count = requestedCount ?? fallbackCount;
        if (count is null || count == 0)
            return (null, null);
        if (count < 0 || count > MaxCount)
            throw new DomainException(InvalidCountMessage);

        var seconds = requestedSeconds ?? fallbackSeconds ?? DefaultSeconds;
        if (seconds < MinSeconds || seconds > MaxSeconds)
            throw new DomainException(InvalidSecondsMessage);
        return (count, seconds);
    }

    /// <summary>Timebanks com que uma inscrição nova nasce (0 sem timebank)</summary>
    public static int Initial(Tournament tournament) => tournament.TimebankCount ?? 0;

    public static string UsedDescription(int seconds, int remaining) =>
        $"Timebank usado ({seconds} s) · {RemainingText(remaining)}";

    public static string AdjustedDescription(int delta, int remaining) =>
        $"Timebank {(delta > 0 ? "+1" : "−1")} · {RemainingText(remaining)}";

    private static string RemainingText(int remaining) => remaining switch
    {
        0 => "nenhum restante",
        1 => "1 restante",
        _ => $"{remaining} restantes"
    };
}
