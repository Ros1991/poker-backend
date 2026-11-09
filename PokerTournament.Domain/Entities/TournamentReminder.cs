namespace PokerTournament.Domain.Entities;

/// <summary>
/// Lembrete do dia já enviado (SPEC-20261108-1639): uma linha por conta e torneio garante que a notificação
/// <c>TournamentReminder</c> sai UMA vez, mesmo com o job rodando a cada 10 minutos o dia inteiro. Não é soft-deletable
/// (é só um marcador).
/// </summary>
public class TournamentReminder : BaseEntity
{
    public Guid TournamentId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset SentAt { get; set; }
}
