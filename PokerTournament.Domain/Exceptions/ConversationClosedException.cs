namespace PokerTournament.Domain.Exceptions;

/// <summary>
/// A conversa existe, mas a regra de quem pode trocar mensagens deixou de valer (revisão da onda 2, M1): a amizade foi
/// desfeita, quem recebe mudou <c>MessagesFrom</c>, ou o pedido de vaga do torneio não está mais em aberto. O
/// ErrorHandlingMiddleware responde 400 com <c>code: "conversation_closed"</c> para a tela desabilitar a caixa de envio
/// com o motivo (a conversa continua legível para os dois).
/// </summary>
public sealed class ConversationClosedException : DomainException
{
    public const string ErrorCode = "conversation_closed";

    public string Code => ErrorCode;

    public ConversationClosedException(string message) : base(message)
    {
    }
}
