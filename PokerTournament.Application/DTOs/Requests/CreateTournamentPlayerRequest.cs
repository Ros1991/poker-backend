namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// Cadastro de jogador novo pelo torneio (painel ou terminal do dealer): nome completo obrigatório, apelido, WhatsApp e
/// e-mail opcionais. O e-mail (SPEC-20261029-1725) liga o jogador à conta confirmada com o mesmo e-mail; o dealer por QR
/// não informa e-mail (ignorado para o token de escopo restrito). A foto vai depois, por POST
/// tournaments/{id}/players/{personId}/photo.
/// </summary>
public class CreateTournamentPlayerRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Whatsapp { get; set; }
    public string? Email { get; set; }
}
