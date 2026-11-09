namespace PokerTournament.Domain.Entities;

/// <summary>
/// Pedido de vaga num torneio público (SPEC-20261108-1639, D7): uma conta logada pede para jogar (mensagem opcional);
/// quem gerencia o home game aprova — vira RESERVA: o jogador (<see cref="PersonId"/>) é criado no clube com nome,
/// apelido e e-mail da conta (ou reaproveitado, se a conta já tem jogador vinculado lá) e entra na fila "Reservas" do
/// painel e do terminal, sem inscrição nem transação até o organizador inscrever (<see cref="EntryId"/> então aponta a
/// inscrição; desfazer a inscrição devolve a reserva à fila — FK SetNull). Com as vagas (<c>Tournament.MaxPlayers</c>)
/// esgotadas, a aprovação põe em espera (<see cref="WaitlistPosition"/>) e a primeira da fila sobe sozinha quando uma
/// reserva sai. Um pedido aberto (Pending/Approved/Waitlisted) por conta e torneio (índice único parcial); cancelado
/// pode pedir de novo, recusado não. Não é soft-deletable: os decididos ficam como registro do que aconteceu.
/// </summary>
public class TournamentJoinRequest : BaseEntity
{
    public const int MessageMaxLength = 500;

    public Guid TournamentId { get; set; }
    /// <summary>Conta que pediu a vaga.</summary>
    public Guid UserId { get; set; }
    /// <summary>Recado para o organizador (até 500).</summary>
    public string? Message { get; set; }
    /// <summary><see cref="Enums.JoinRequestStatus"/> via nameof.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Posição na lista de espera (1 = a próxima a subir); null fora da espera.</summary>
    public int? WaitlistPosition { get; set; }
    /// <summary>Jogador do clube criado/reaproveitado na aprovação (vinculado à conta).</summary>
    public Guid? PersonId { get; set; }
    /// <summary>Inscrição feita a partir da reserva (null = ainda na fila "Reservas").</summary>
    public Guid? EntryId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    /// <summary>Quem decidiu (aprovou, recusou ou removeu a reserva); null no cancelamento pelo interessado e na promoção automática.</summary>
    public Guid? DecidedBy { get; set; }

    // Navigation
    public Tournament Tournament { get; set; } = null!;
    public User User { get; set; } = null!;
    public Person? Person { get; set; }
    public TournamentEntry? Entry { get; set; }
}
