namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// Add-on (SPEC-20261027-1311): quantos add-ons comprar de uma vez (1 a 10, como o rebuy), até o máximo do torneio
/// (Tournament.MaxAddons; 0 = ilimitado). Sem corpo ou sem o campo = 1. O antigo IsDouble saiu (cada add-on é uma unidade).
/// </summary>
public class AddonRequest
{
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Fora do período (SPEC-20261105-1707: intervalo de acerto): sem isto o pedido volta 409 <c>outside_window</c>; com
    /// true, lança e marca o lançamento como "fora do período" no histórico. Dentro do período não muda nada.
    /// </summary>
    public bool ConfirmOutsideWindow { get; set; }
}
