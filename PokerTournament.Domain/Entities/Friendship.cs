using PokerTournament.Domain.Enums;

namespace PokerTournament.Domain.Entities;

/// <summary>
/// Um registro por par de contas (SPEC-20261108-1637): o par é guardado ordenado (<see cref="UserLowId"/> &lt;
/// <see cref="UserHighId"/>, índice único) para nunca existirem duas linhas entre as mesmas pessoas em direções opostas.
/// <see cref="RequesterId"/> diz quem pediu (ou quem bloqueou); <see cref="Status"/> é <see cref="FriendshipStatus"/>
/// via nameof. Não é soft-deletable de propósito: desfazer a amizade e desbloquear APAGAM a linha (o par volta a ser
/// de desconhecidos). Recusa fica gravada (<c>Declined</c> + <see cref="RespondedAt"/>) para a carência de 30 dias, e o
/// cancelamento também (<c>Cancelled</c> + <see cref="RespondedAt"/>) para pedir de novo em menos de 24 h não notificar
/// de novo (revisão da onda 2, M3).
/// </summary>
public class Friendship : BaseEntity
{
    /// <summary>O menor dos dois ids do par.</summary>
    public Guid UserLowId { get; set; }
    /// <summary>O maior dos dois ids do par.</summary>
    public Guid UserHighId { get; set; }
    /// <summary>Quem pediu a amizade; no bloqueio, quem bloqueou.</summary>
    public Guid RequesterId { get; set; }
    public string Status { get; set; } = nameof(FriendshipStatus.Pending);
    /// <summary>Quando o pedido foi feito (ou renovado depois da carência); no bloqueio, quando bloqueou.</summary>
    public DateTimeOffset RequestedAt { get; set; }
    /// <summary>Quando quem recebeu aceitou ou recusou, ou quem pediu cancelou (null enquanto pendente e no bloqueio).</summary>
    public DateTimeOffset? RespondedAt { get; set; }
    /// <summary>Quem bloqueou (só em <c>Blocked</c>); só essa conta desbloqueia.</summary>
    public Guid? BlockedById { get; set; }

    // Navigation
    public User UserLow { get; set; } = null!;
    public User UserHigh { get; set; } = null!;

    /// <summary>Par ordenado (low, high) de duas contas.</summary>
    public static (Guid Low, Guid High) Order(Guid a, Guid b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);

    /// <summary>A conta faz parte deste par.</summary>
    public bool Involves(Guid userId) => UserLowId == userId || UserHighId == userId;

    /// <summary>A outra conta do par.</summary>
    public Guid OtherOf(Guid userId) => UserLowId == userId ? UserHighId : UserLowId;
}
