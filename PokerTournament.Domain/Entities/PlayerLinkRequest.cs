namespace PokerTournament.Domain.Entities;

/// <summary>
/// Pedido de vínculo "sou eu" (SPEC-20261108-1632): uma conta logada leu o convite do torneio
/// (<see cref="TournamentInvite"/>) e diz ser um dos inscritos ainda sem conta (<see cref="PersonId"/>), ou avisa que
/// não está na lista (<see cref="PersonId"/> nulo). O organizador aprova (grava <c>Person.UserId</c> e, se o cadastro
/// estava sem e-mail, copia o da conta) ou recusa. Vence em 24 h sem decisão (Expired ao ler). Um pendente por conta e
/// torneio (índice único parcial).
/// </summary>
public class PlayerLinkRequest : BaseEntity
{
    public Guid TournamentId { get; set; }
    /// <summary>Jogador escolhido; nulo = "não estou na lista".</summary>
    public Guid? PersonId { get; set; }
    /// <summary>Conta que pediu.</summary>
    public Guid UserId { get; set; }
    /// <summary>Convite lido no pedido (sem FK obrigatória: o convite pode ser renovado depois).</summary>
    public Guid? InviteId { get; set; }
    public string Status { get; set; } = string.Empty;   // PlayerLinkRequestStatus
    /// <summary>Observação curta do pedido ("não está na lista") ou da decisão (e-mail copiado / mantido).</summary>
    public string? Message { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    /// <summary>Quem decidiu; nulo quando venceu sozinho ou foi recusado automaticamente.</summary>
    public Guid? DecidedBy { get; set; }
    /// <summary>Na aprovação: o e-mail da conta foi copiado para o cadastro do jogador (estava vazio).</summary>
    public bool EmailCopied { get; set; }

    // Navigation
    public Person? Person { get; set; }
    public User User { get; set; } = null!;
}
