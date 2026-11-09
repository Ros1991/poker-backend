using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PokerTournament.Application.Common;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Hubs;

/// <summary>
/// Hub das mensagens 1 para 1 (SPEC-20261108-1638), em <c>/hubs/chat</c>. <c>[Authorize]</c> com o
/// <c>TokenKindRequirement</c> da política padrão e SEM <c>[AllowTokenKind]</c>: o token de dealer (QR) não conecta
/// (403 no negotiate). Toda conexão de conta entra no grupo <c>user_{id}</c> ao conectar; é para esse grupo que o
/// <c>ChatController</c> difunde <c>MessageReceived</c>, <c>MessageRead</c> e <c>MessageDeleted</c> depois de gravar
/// (convenção do projeto: broadcast no controller, nunca no serviço). Métodos do cliente: <see cref="OpenConversation"/> /
/// <see cref="CloseConversation"/> (presença, para a notificação só nascer com a conversa fechada) e <see cref="Typing"/>
/// (repassado à outra conta, no máximo 1 a cada 2 s por conexão). Nada é gravado por aqui: enviar, ler e apagar são REST.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private const string NoAccessMessage = "Conversa não encontrada.";

    private readonly ChatService _chat;
    private readonly ChatConnectionRegistry _connections;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(ChatService chat, ChatConnectionRegistry connections, ILogger<ChatHub> logger)
    {
        _chat = chat;
        _connections = connections;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        if (TryGetUserId(Context.User, out var userId))
        {
            _connections.Add(Context.ConnectionId, userId);
            await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.ForUser(userId));
            _logger.LogInformation("Conexão {ConnectionId} entrou no chat da conta {UserId}", Context.ConnectionId, userId);
        }
        else
        {
            // Token sem conta (não deveria chegar aqui: a política padrão barra o dealer); sem grupo, sem registro
            _logger.LogWarning("Conexão {ConnectionId} no ChatHub sem conta: derrubada", Context.ConnectionId);
            Context.Abort();
            return;
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>A tela da conversa abriu nesta conexão: as mensagens que chegarem não viram notificação.</summary>
    public async Task OpenConversation(Guid conversationId)
    {
        var userId = RequireUserId();
        if (await _chat.FindOtherAsync(userId, conversationId, Context.ConnectionAborted) is null)
            throw new HubException(NoAccessMessage);
        _connections.Open(Context.ConnectionId, conversationId);
    }

    /// <summary>A tela da conversa fechou nesta conexão.</summary>
    public Task CloseConversation(Guid conversationId)
    {
        RequireUserId();
        _connections.Close(Context.ConnectionId, conversationId);
        return Task.CompletedTask;
    }

    /// <summary>Repassa "digitando" à outra conta da conversa (1 a cada 2 s por conexão; o excesso é ignorado em silêncio).</summary>
    public async Task Typing(Guid conversationId)
    {
        var userId = RequireUserId();
        if (!_connections.TryTyping(Context.ConnectionId, DateTimeOffset.UtcNow))
            return;
        var otherId = await _chat.FindOtherAsync(userId, conversationId, Context.ConnectionAborted);
        if (otherId is null)
            throw new HubException(NoAccessMessage);
        await Clients.Group(NotificationGroups.ForUser(otherId.Value))
            .SendAsync(ChatEvents.Typing, new { conversationId, userId }, Context.ConnectionAborted);
    }

    private Guid RequireUserId() =>
        TryGetUserId(Context.User, out var userId) ? userId : throw new HubException("Acesso não autorizado.");

    /// <summary>Id da conta de um token de usuário (sem o claim kind). False para token de escopo restrito (dealer).</summary>
    private static bool TryGetUserId(ClaimsPrincipal? user, out Guid userId)
    {
        userId = Guid.Empty;
        if (user is null || !string.IsNullOrEmpty(user.FindFirst(TokenKinds.ClaimType)?.Value))
            return false;
        var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
