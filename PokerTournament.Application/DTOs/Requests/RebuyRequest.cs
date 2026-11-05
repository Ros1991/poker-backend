namespace PokerTournament.Application.DTOs.Requests;

public class RebuyRequest
{
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Fora do período (SPEC-20261105-1707: depois do "rebuy até o nível N" ou no intervalo de acerto): sem isto o pedido
    /// volta 409 <c>outside_window</c>; com true, lança e marca o lançamento como "fora do período" no histórico. Dentro do
    /// período não muda nada.
    /// </summary>
    public bool ConfirmOutsideWindow { get; set; }
}
