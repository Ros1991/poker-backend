using System.Collections.Concurrent;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Api.Hubs;

/// <summary>
/// Conexões abertas do <see cref="ChatHub"/> (SPEC-20261108-1638), nos moldes do <see cref="DealerConnectionRegistry"/>:
/// para cada conexão, a conta e a conversa que ela está com a tela aberta (<c>OpenConversation</c>/<c>CloseConversation</c>).
/// É a implementação de <see cref="IChatPresence"/>: a mensagem nova só vira notificação quando NENHUMA conexão de quem
/// recebe está com aquela conversa aberta. Também guarda o último "digitando" de cada conexão (1 a cada 2 s). Em memória:
/// vale para a instância da API (o deploy é instância única). Preenchido no OnConnectedAsync e limpo no OnDisconnectedAsync.
/// </summary>
public sealed class ChatConnectionRegistry : IChatPresence
{
    /// <summary>Intervalo mínimo entre dois "digitando" da mesma conexão.</summary>
    public static readonly TimeSpan TypingInterval = TimeSpan.FromSeconds(2);

    private sealed class Entry
    {
        public Guid UserId { get; init; }
        public Guid? OpenConversationId { get; set; }
        public DateTimeOffset LastTypingAt { get; set; } = DateTimeOffset.MinValue;
    }

    private readonly ConcurrentDictionary<string, Entry> _byConnection = new();

    public void Add(string connectionId, Guid userId) =>
        _byConnection[connectionId] = new Entry { UserId = userId };

    public void Remove(string connectionId) => _byConnection.TryRemove(connectionId, out _);

    /// <summary>A conexão abriu a tela da conversa (fecha a anterior, se havia).</summary>
    public void Open(string connectionId, Guid conversationId)
    {
        if (_byConnection.TryGetValue(connectionId, out var entry))
            entry.OpenConversationId = conversationId;
    }

    /// <summary>A conexão saiu da tela da conversa (só se era essa a aberta).</summary>
    public void Close(string connectionId, Guid conversationId)
    {
        if (_byConnection.TryGetValue(connectionId, out var entry) && entry.OpenConversationId == conversationId)
            entry.OpenConversationId = null;
    }

    public bool IsConversationOpen(Guid userId, Guid conversationId) =>
        _byConnection.Values.Any(e => e.UserId == userId && e.OpenConversationId == conversationId);

    /// <summary>Verdadeiro se a conexão pode mandar "digitando" agora (e marca o instante); falso dentro dos 2 s.</summary>
    public bool TryTyping(string connectionId, DateTimeOffset now)
    {
        if (!_byConnection.TryGetValue(connectionId, out var entry))
            return false;
        lock (entry)
        {
            if (now - entry.LastTypingAt < TypingInterval)
                return false;
            entry.LastTypingAt = now;
            return true;
        }
    }

    /// <summary>Quantas conexões a conta tem abertas (diagnóstico).</summary>
    public int CountConnections(Guid userId) => _byConnection.Values.Count(e => e.UserId == userId);
}
