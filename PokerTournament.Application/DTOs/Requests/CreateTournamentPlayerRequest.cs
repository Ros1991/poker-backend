namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// Cadastro de jogador novo pelo torneio (painel ou terminal do dealer): nome completo obrigatório, apelido e
/// WhatsApp opcionais. A foto vai depois, por POST tournaments/{id}/players/{personId}/photo.
/// </summary>
public class CreateTournamentPlayerRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Whatsapp { get; set; }
}
