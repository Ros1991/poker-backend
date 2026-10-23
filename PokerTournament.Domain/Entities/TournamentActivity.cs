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
}
