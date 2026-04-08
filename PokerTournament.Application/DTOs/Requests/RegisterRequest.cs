namespace PokerTournament.Application.DTOs.Requests;

public class RegisterRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Whatsapp { get; set; }
}
