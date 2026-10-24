namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// Token do link "Confirme seu e-mail" ({PUBLIC_APP_URL}/confirmar-email/{token}) e a senha escolhida no cadastro que
/// gerou o link (D16.10): só token + senha certos confirmam a conta (e já entram nela).
/// </summary>
public class ConfirmEmailRequest
{
    public string Token { get; set; } = string.Empty;
    /// <summary>Opcional no modelo de propósito: sem senha cai no mesmo 400 "Senha incorreta." (e conta no limite).</summary>
    public string? Password { get; set; }
}
