namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// GET HomeGames/{id}/deletion-summary (SPEC-20261026-1126): o que some junto com o home game, para o modal
/// "Excluir home game" mostrar antes de confirmar. Só o dono e o Admin do sistema leem.
/// </summary>
public class HomeGameDeletionSummaryResponse
{
    public Guid HomeGameId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string? OwnerName { get; set; }
    /// <summary>Torneios do home game (inclusive encerrados e cancelados)</summary>
    public int Tournaments { get; set; }
    /// <summary>Jogadores cadastrados (inclusive os marcados como Dealer)</summary>
    public int Players { get; set; }
    public int Rankings { get; set; }
    /// <summary>Membros além do dono (perdem o acesso)</summary>
    public int Members { get; set; }
    /// <summary>Torneio em andamento que impede a exclusão (null = pode excluir)</summary>
    public Guid? BlockingTournamentId { get; set; }
    public string? BlockingTournamentName { get; set; }
    /// <summary>"Encerre ou cancele o torneio X antes de excluir o home game." quando há bloqueio</summary>
    public string? BlockedMessage { get; set; }
}
