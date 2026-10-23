namespace PokerTournament.Application.DTOs.Requests;

/// <summary>Token do link "Confirme seu e-mail" ({PUBLIC_APP_URL}/confirmar-email/{token}).</summary>
public class ConfirmEmailRequest
{
    public string Token { get; set; } = string.Empty;
}
