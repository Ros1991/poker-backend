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
    public string? By { get; set; }                  // "Dealer · Nome", "Dealer" (acesso antigo) ou o nome do usuário
    public bool Undone { get; set; }
    public DateTimeOffset? UndoneAt { get; set; }
    public string? UndoneBy { get; set; }
    public bool CanUndo { get; set; }
    public string? UndoBlockedReason { get; set; }
    // Aviso para quem PODE desfazer mesmo assim (organizador com token de usuário: rebuy, add-on ou inscrição com o
    // torneio encerrado/cancelado ou prêmio pago — SPEC-20261023-1217, L4); a tela mostra na confirmação. Null = sem aviso.
    public string? UndoWarning { get; set; }
    // Só na eliminação (Kind == "Elimination"): posição em que saiu e quem eliminou (apelido, senão nome). A confirmação
    // do desfazer monta a frase com eles ("Desfazer a eliminação de Duda (10º lugar, quem eliminou foi Beto)?").
    public int? Position { get; set; }
    public string? EliminatedByName { get; set; }
    // Só nas atividades de timebank (SPEC-20261029-1722): −1 no uso, +1/−1 no ajuste. A confirmação do desfazer diz o que
    // volta ("o timebank volta para o jogador").
    public int? Delta { get; set; }
}

public class UndoResultResponse
{
    public string Message { get; set; } = string.Empty;
}
