using PokerTournament.Domain.Enums;

namespace PokerTournament.Domain.Entities;

/// <summary>
/// Conversa 1 para 1 (SPEC-20261108-1638). O par é guardado ordenado (<see cref="UserLowId"/> &lt; <see cref="UserHighId"/>,
/// como em <see cref="Friendship"/>) e há no máximo uma conversa <c>Direct</c> por par e uma <c>TournamentRequest</c> por
/// par e torneio (índices únicos parciais). <see cref="CreatedById"/> diz quem abriu (limite de 20 conversas novas por dia
/// por conta). Não é soft-deletable: bloqueio esconde a conversa pelo <c>IFriendshipLookup</c>, sem apagar nada.
/// </summary>
public class Conversation : BaseEntity
{
    /// <summary><see cref="ConversationType"/> via nameof.</summary>
    public string Type { get; set; } = nameof(ConversationType.Direct);
    /// <summary>Torneio público do pedido de vaga (só em <c>TournamentRequest</c>).</summary>
    public Guid? TournamentId { get; set; }
    /// <summary>O menor dos dois ids do par.</summary>
    public Guid UserLowId { get; set; }
    /// <summary>O maior dos dois ids do par.</summary>
    public Guid UserHighId { get; set; }
    /// <summary>Quem abriu a conversa.</summary>
    public Guid CreatedById { get; set; }
    /// <summary>Quando a última mensagem foi enviada (ordena a lista; null = conversa sem mensagem).</summary>
    public DateTimeOffset? LastMessageAt { get; set; }

    // Navigation
    public User UserLow { get; set; } = null!;
    public User UserHigh { get; set; } = null!;
    public Tournament? Tournament { get; set; }
    public ICollection<ConversationMember> Members { get; set; } = [];
    public ICollection<ChatMessage> Messages { get; set; } = [];

    /// <summary>Par ordenado (low, high) de duas contas.</summary>
    public static (Guid Low, Guid High) Order(Guid a, Guid b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);

    /// <summary>A conta faz parte desta conversa.</summary>
    public bool Involves(Guid userId) => UserLowId == userId || UserHighId == userId;

    /// <summary>A outra conta da conversa.</summary>
    public Guid OtherOf(Guid userId) => UserLowId == userId ? UserHighId : UserLowId;
}

/// <summary>
/// Participação de uma conta numa conversa (SPEC-20261108-1638): guarda até quando ela leu (<see cref="LastReadAt"/>).
/// Mensagem da outra conta com <c>SentAt</c> depois disso é "não lida"; quem envia tem o próprio LastReadAt avançado no envio.
/// </summary>
public class ConversationMember : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset? LastReadAt { get; set; }

    // Navigation
    public Conversation Conversation { get; set; } = null!;
    public User User { get; set; } = null!;
}

/// <summary>
/// Mensagem de texto (SPEC-20261108-1638), até 2.000 caracteres; chama-se ChatMessage porque o SDK da Anthropic já tem um Message no Infrastructure. NÃO herda de SoftDeletableEntity de propósito: a mensagem
/// apagada pela própria pessoa (<see cref="DeletedAt"/>) continua na lista como "mensagem apagada" — o filtro global a
/// esconderia. O texto apagado fica no banco (a denúncia guarda a própria cópia no momento em que é feita).
/// </summary>
public class ChatMessage : BaseEntity
{
    public const int BodyMaxLength = 2000;

    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
    /// <summary>Quando quem enviou apagou a mensagem (null = visível).</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigation
    public Conversation Conversation { get; set; } = null!;
    public User Sender { get; set; } = null!;
}
