namespace PokerTournament.Domain.Entities;

public class TournamentTable : BaseEntity
{
    public Guid TournamentId { get; set; }
    public int TableNumber { get; set; }
    public string? TableName { get; set; }
    public int MaxSeats { get; set; }
    public Guid? DealerPersonId { get; set; }
    public bool IsFinalTable { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Tournament Tournament { get; set; } = null!;
    public Person? DealerPerson { get; set; }
    public ICollection<TournamentEntry> Entries { get; set; } = [];
}
