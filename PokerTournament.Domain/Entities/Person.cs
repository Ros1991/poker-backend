namespace PokerTournament.Domain.Entities;

public class Person : SoftDeletableEntity
{
    public Guid HomeGameId { get; set; }
    public string Type { get; set; } = "Jogador"; // Jogador, Dealer
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    /// <summary>Foto do cadastro neste home game. A foto EXIBIDA é a da conta vinculada, se ela tiver (PlayerPhoto).</summary>
    public string? PhotoUrl { get; set; }
    public string? Document { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Conta de login deste jogador (SPEC-20261029-1725): ligada pelo e-mail confirmado igual ao do cadastro
    /// (PlayerLinkService) ou pelo "vincular membro ↔ jogador". A conta é o hub do jogador entre home games: a foto dela
    /// vale em todos. Null = sem conta. Conta excluída (anonimizada) solta os jogadores.
    /// </summary>
    public Guid? UserId { get; set; }

    // Navigation
    public HomeGame HomeGame { get; set; } = null!;
    public User? User { get; set; }
    public ICollection<HomeGameMember> HomeGameMembers { get; set; } = [];
    public ICollection<TournamentEntry> TournamentEntries { get; set; } = [];
}
