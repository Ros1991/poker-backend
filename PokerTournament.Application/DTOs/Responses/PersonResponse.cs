namespace PokerTournament.Application.DTOs.Responses;

public class PersonResponse
{
    public Guid Id { get; set; }
    public Guid HomeGameId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Document { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
