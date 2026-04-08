namespace PokerTournament.Domain.Entities;

public class Person : SoftDeletableEntity
{
    public Guid HomeGameId { get; set; }
    public string Type { get; set; } = "Jogador"; // Jogador, Dealer
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Document { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public HomeGame HomeGame { get; set; } = null!;
    public ICollection<HomeGameMember> HomeGameMembers { get; set; } = [];
    public ICollection<TournamentEntry> TournamentEntries { get; set; } = [];
}
