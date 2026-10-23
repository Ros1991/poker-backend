namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// Item do histórico do torneio (transações + eliminações + atividades), do mais recente
/// para o mais antigo. Description já vem pronta em pt-BR.
/// </summary>
public class HistoryItemResponse
{
    public string Id { get; set; } = string.Empty;   // "{kind}:{refId}"
    public string Kind { get; set; } = string.Empty; // BuyIn|Rebuy|Addon|Payment|Settlement|PrizePayment|Elimination|Activity
    public string? ActivityType { get; set; }        // quando Kind == "Activity"
    public Guid RefId { get; set; }                  // id da transação / eliminação / atividade
    public Guid? EntryId { get; set; }
    public string? PlayerName { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public DateTimeOffset At { get; set; }
    public string? By { get; set; }                  // "Dealer" ou o nome do usuário
    public bool Undone { get; set; }
    public DateTimeOffset? UndoneAt { get; set; }
    public string? UndoneBy { get; set; }
    public bool CanUndo { get; set; }
    public string? UndoBlockedReason { get; set; }
}

public class UndoResultResponse
{
    public string Message { get; set; } = string.Empty;
}
