namespace PokerTournament.Application.DTOs.Requests;

/// <summary>Pedido de um novo link de confirmação (resposta sempre neutra).</summary>
public class ResendConfirmationRequest
{
    public string Email { get; set; } = string.Empty;
}
