namespace PokerTournament.Domain.Entities;

/// <summary>
/// Registro do que acontece no torneio e não é dinheiro (mesas sorteadas, jogador movido,
/// sentado, mesa quebrada, inscrição desfeita). Junto com transações e eliminações forma o
/// histórico do terminal. EntryId sem FK: a inscrição pode ser apagada e o registro fica.
/// </summary>
public class TournamentActivity : BaseEntity
{
    public Guid TournamentId { get; set; }
    public Guid? EntryId { get; set; }
    public string Type { get; set; } = string.Empty;        // nameof(TournamentActivityType.X)
    public string Description { get; set; } = string.Empty; // texto pronto em pt-BR
    public Guid? CreatedBy { get; set; }                     // usuário OU acesso de dealer (sem FK)
    // Detalhe por jogador de algo já resumido em outra linha (cada um da mesa quebrada, o outro
    // lado da troca de lugar): aparece só no histórico do jogador, não no do torneio
    public bool PlayerOnly { get; set; }

    // Timebank (SPEC-20261029-1722): quanto a atividade mudou nos timebanks do jogador (−1 no uso; +1/−1 no ajuste do
    // dealer). Null nas outras atividades.
    public int? Delta { get; set; }
    // Desfeita pelo histórico (só as de timebank se desfazem): quando e quem (usuário OU acesso de dealer, sem FK)
    public DateTimeOffset? UndoneAt { get; set; }
    public Guid? UndoneBy { get; set; }
}
