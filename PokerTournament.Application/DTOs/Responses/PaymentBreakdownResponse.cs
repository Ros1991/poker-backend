namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// Detalhamento financeiro dos pagamentos do torneio (SPEC-20261026-1906): o que entrou por forma de pagamento e para
/// onde foi cada real (caixa ou custo), com quem pagou. Só pagamentos e abatimentos que valem (estornados ficam de
/// fora). Invariantes: soma de <see cref="ByMethod"/> = soma de <see cref="Destinations"/>.ReceivedFromPlayers =
/// <see cref="TotalReceived"/> — nenhum valor fica sem destino.
/// </summary>
public class PaymentBreakdownResponse
{
    /// <summary>Soma dos pagamentos e abatimentos que valem</summary>
    public decimal TotalReceived { get; set; }
    /// <summary>Soma do "pago" das inscrições (o "Recebido" do resumo); bate com <see cref="TotalReceived"/></summary>
    public decimal TotalPaidByEntries { get; set; }
    /// <summary>PIX e Dinheiro sempre; Transferência, Abatimento e Outras formas só quando houver</summary>
    public List<PaymentMethodTotal> ByMethod { get; set; } = [];
    /// <summary>Caixa primeiro, depois os custos na ordem da aba Custos; "Custo excluído" no fim, se houver</summary>
    public List<PaymentDestinationDetail> Destinations { get; set; } = [];
}

public class PaymentMethodTotal
{
    /// <summary>Pix | Dinheiro | Transferencia | Abatimento | Outros</summary>
    public string Method { get; set; } = string.Empty;
    /// <summary>PIX | Dinheiro | Transferência | Abatimento | Outras formas</summary>
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    /// <summary>Lançamentos com alguma parte nesta forma (o misto conta no PIX e no dinheiro)</summary>
    public int Count { get; set; }
}

public class PaymentDestinationDetail
{
    /// <summary>CashBox (caixa / gestor financeiro) | Cost (custo do torneio) | Removed (custo que foi excluído)</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>null no caixa e no custo excluído</summary>
    public Guid? CostExtraId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Beneficiary { get; set; }
    public string? CostType { get; set; }
    /// <summary>O que o custo deveria receber (null no caixa e no custo excluído)</summary>
    public decimal? Amount { get; set; }
    /// <summary>Quanto o custo já tem pago, por jogadores e pelo caixa (null no caixa)</summary>
    public decimal? PaidAmount { get; set; }
    /// <summary>Soma das linhas: o que os jogadores pagaram direto para este destino (inclui abatimentos)</summary>
    public decimal ReceivedFromPlayers { get; set; }
    /// <summary>Parte de <see cref="ReceivedFromPlayers"/> que veio por abatimento</summary>
    public decimal SettledAmount { get; set; }
    /// <summary>Pago pelo caixa ao custo (botão "Pagar" da aba Custos): PaidAmount − o que veio dos jogadores</summary>
    public decimal PaidByCashBox { get; set; }
    /// <summary>Quanto o custo ainda tem a receber (null no caixa)</summary>
    public decimal? Remaining { get; set; }
    public string? PaymentStatus { get; set; }
    public List<PaymentDestinationLine> Lines { get; set; } = [];
}

public class PaymentDestinationLine
{
    public Guid TransactionId { get; set; }
    public Guid? EntryId { get; set; }
    /// <summary>Apelido (ou o nome completo, sem apelido)</summary>
    public string PlayerName { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? PhotoUrl { get; set; }
    /// <summary>Parte do lançamento que foi para este destino</summary>
    public decimal Amount { get; set; }
    /// <summary>Forma desta parte: Pix | Dinheiro | Misto | Transferencia | Abatimento | Outros</summary>
    public string Method { get; set; } = string.Empty;
    /// <summary>PIX | Dinheiro | PIX + dinheiro | Transferência | Abatimento | Outra forma</summary>
    public string MethodLabel { get; set; } = string.Empty;
    public bool IsSettlement { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Totais da aba Pagamentos (cards PIX, Dinheiro, Pago para Custos, Pendente)</summary>
public class PaymentTotalsByMethodResponse
{
    public decimal TotalPix { get; set; }
    public decimal TotalCash { get; set; }
    public decimal TotalToCosts { get; set; }
    public decimal TotalPending { get; set; }
}
