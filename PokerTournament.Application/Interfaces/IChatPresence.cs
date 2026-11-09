namespace PokerTournament.Application.Interfaces;

/// <summary>
/// Quem está com qual conversa aberta neste momento (SPEC-20261108-1638). A implementação fica na API
/// (<c>ChatConnectionRegistry</c>, alimentado pelos métodos <c>OpenConversation</c>/<c>CloseConversation</c> do
/// <c>ChatHub</c>): a camada Application só pergunta, para decidir se a mensagem nova vira notificação — ela só nasce
/// quando NENHUMA conexão de quem recebe está com a conversa aberta (a pessoa está lendo em tempo real). Em memória,
/// válido para a instância da API (o deploy é instância única).
/// </summary>
public interface IChatPresence
{
    /// <summary>Alguma conexão da conta está com a conversa aberta.</summary>
    bool IsConversationOpen(Guid userId, Guid conversationId);
}

/// <summary>Nomes dos eventos do <c>ChatHub</c> compartilhados pelo controller, pelo hub e pelo frontend.</summary>
public static class ChatEvents
{
    /// <summary>Mensagem nova numa conversa da conta: <c>{ conversationId, message, unreadConversations }</c>.</summary>
    public const string MessageReceived = "MessageReceived";
    /// <summary>A outra conta leu até <c>readAt</c>: <c>{ conversationId, userId, readAt }</c>.</summary>
    public const string MessageRead = "MessageRead";
    /// <summary>Quem enviou apagou a mensagem: <c>{ conversationId, messageId }</c>.</summary>
    public const string MessageDeleted = "MessageDeleted";
    /// <summary>A outra conta está digitando: <c>{ conversationId, userId }</c> (no máximo 1 a cada 2 s por conexão).</summary>
    public const string Typing = "Typing";
}
