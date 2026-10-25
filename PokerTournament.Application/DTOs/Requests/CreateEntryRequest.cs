namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// Inscrição individual. Sem "buy-in pago" (SPEC-20261023-1213, F15): toda inscrição nasce Pendente e o dinheiro
/// entra pela tela de pagamentos; um "buyInPaid" que um cliente antigo mande é ignorado pelo System.Text.Json.
/// </summary>
public class CreateEntryRequest
{
    public Guid PersonId { get; set; }
}
