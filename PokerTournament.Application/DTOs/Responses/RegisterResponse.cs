namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// Resposta do cadastro (202): a conta existe, mas só entra depois de confirmar o e-mail —
/// por isso não há tokens aqui. <see cref="Email"/> é o e-mail gravado (minúsculas).
/// </summary>
public class RegisterResponse
{
    public string Email { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
