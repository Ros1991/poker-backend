namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// Inscrição em lote (painel e terminal). Sem "buy-in pago" (SPEC-20261023-1213, F15): todas nascem Pendentes;
/// um "buyInPaid" que um cliente antigo mande é ignorado pelo System.Text.Json.
/// </summary>
public class BulkCreateEntriesRequest
{
    public List<Guid> PersonIds { get; set; } = [];
}
