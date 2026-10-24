namespace PokerTournament.Application.DTOs.Requests;

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>
    /// Ticket do 409 <c>link_requires_password</c> do login social (SPEC-20261023-1210): com a senha certa da
    /// conta, conecta o login do provedor a ela e entra. Opcional — sem ele é o login de sempre.
    /// </summary>
    public string? LinkTicket { get; set; }
}
