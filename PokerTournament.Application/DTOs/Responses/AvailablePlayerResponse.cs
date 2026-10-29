namespace PokerTournament.Application.DTOs.Responses;

/// <summary>Jogador do home game que ainda não está no torneio. Sem contato/documento (o dealer vê).</summary>
public class AvailablePlayerResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    /// <summary>Foto EXIBIDA: a da conta vinculada, se ela tiver; senão a do cadastro (SPEC-20261029-1725, PlayerPhoto).</summary>
    public string? PhotoUrl { get; set; }
}
