namespace PokerTournament.Domain.Entities;

public class HomeGameMember : BaseEntity
{
    public Guid HomeGameId { get; set; }
    public Guid UserId { get; set; }
    public Guid? PersonId { get; set; }
    public bool IsAdmin { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public HomeGame HomeGame { get; set; } = null!;
    public User User { get; set; } = null!;
    public Person? Person { get; set; }
}
