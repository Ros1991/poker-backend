namespace PokerTournament.Domain.Entities;

public class NotificationLog : BaseEntity
{
    public Guid? PersonId { get; set; }
    public Guid? TournamentId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Message { get; set; }
    public string? TemplateKey { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? SentAt { get; set; }
    public string? ErrorMessage { get; set; }

    // Navigation
    public Person? Person { get; set; }
    public Tournament? Tournament { get; set; }
}
