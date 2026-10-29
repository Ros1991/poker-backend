namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// "Já conhecemos este jogador" (GET persons/lookup-email, SPEC-20261029-1725): a conta confirmada e ativa com o e-mail
/// digitado no cadastro do jogador. Sem id, e-mail ou papel: só o que o formulário mostra e preenche.
/// </summary>
public class AccountLookupResponse
{
    public bool Found { get; set; }
    public string? FullName { get; set; }
    public string? Nickname { get; set; }
    public string? PhotoUrl { get; set; }
}
