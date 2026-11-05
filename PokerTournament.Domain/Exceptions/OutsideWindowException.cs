namespace PokerTournament.Domain.Exceptions;

/// <summary>
/// Rebuy ou add-on pedido fora do período (SPEC-20261105-1707): depois do "rebuy até o nível N" ou com o torneio no
/// intervalo de acerto ("Encerrar Rebuys"). Não é recusa definitiva: o pedido repetido com <c>confirmOutsideWindow: true</c>
/// lança e marca o lançamento como "fora do período". O ErrorHandlingMiddleware traduz em HTTP 409
/// <c>{ code: "outside_window", kind, reason, level, message }</c> — o terminal e o painel perguntam
/// "&lt;message&gt; Lançar mesmo assim?". Não herda de DomainException de propósito (senão viraria 400).
/// </summary>
public class OutsideWindowException : Exception
{
    public const string ErrorCode = "outside_window";

    /// <summary>Período de rebuy acabou no nível <see cref="Level"/> (rebuy até o nível N)</summary>
    public const string ReasonRebuyLevel = "RebuyLevel";

    /// <summary>Torneio no intervalo de acerto (status BreakSettlement, o "Encerrar Rebuys" do painel)</summary>
    public const string ReasonSettlement = "Settlement";

    public string Code => ErrorCode;

    /// <summary>"Rebuy" ou "Addon" (o nome do TransactionType)</summary>
    public string Kind { get; }

    /// <summary><see cref="ReasonRebuyLevel"/> ou <see cref="ReasonSettlement"/></summary>
    public string Reason { get; }

    /// <summary>Último nível do período (o "rebuy até o nível N"); null quando o motivo é o intervalo de acerto</summary>
    public int? Level { get; }

    public OutsideWindowException(string message, string kind, string reason, int? level) : base(message)
    {
        Kind = kind;
        Reason = reason;
        Level = level;
    }
}
