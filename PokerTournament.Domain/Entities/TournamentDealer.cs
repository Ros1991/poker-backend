namespace PokerTournament.Domain.Entities;

public class TournamentDealer : BaseEntity
{
    public Guid TournamentId { get; set; }
    public Guid PersonId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
    public Person Person { get; set; } = null!;
}
