namespace PokerTournament.Application.DTOs.Requests;

public class UpdatePersonRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    public string? Notes { get; set; }
}
