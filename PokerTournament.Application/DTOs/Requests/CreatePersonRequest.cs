namespace PokerTournament.Application.DTOs.Requests;

public class CreatePersonRequest
{
    public Guid HomeGameId { get; set; }
    public string Type { get; set; } = "Jogador";
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Document { get; set; }
    public string? Notes { get; set; }
}
