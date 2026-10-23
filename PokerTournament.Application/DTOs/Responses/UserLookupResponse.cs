namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// Resultado da busca de conta por e-mail exato (modal "Adicionar Membro").
/// Só o necessário para confirmar a pessoa: nunca carrega papel, senha ou status.
/// </summary>
public class UserLookupResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
}
