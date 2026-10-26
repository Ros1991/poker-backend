namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// DELETE HomeGames/{id} (SPEC-20261026-1126): o nome do home game digitado no modal. O servidor confere de novo
/// (sem diferenciar maiúsculas e ignorando espaços nas pontas); vazio ou diferente → 400 sem excluir nada.
/// </summary>
public class DeleteHomeGameRequest
{
    public string? ConfirmName { get; set; }
}
