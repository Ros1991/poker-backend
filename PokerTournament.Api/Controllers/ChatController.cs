using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokerTournament.Api.Hubs;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Mensagens 1 para 1 e denúncias (SPEC-20261108-1638): qualquer conta logada (qualquer papel), sempre sobre as PRÓPRIAS
/// conversas (o id vem do token). Sem escopo de home game e fora do filtro de tenant. Sem [AllowTokenKind]: o token de
/// dealer (QR) não chega aqui (403). As regras estão no <see cref="ChatService"/> e no <see cref="ReportService"/>; depois
/// de gravar, ESTE controller difunde os eventos pelo <see cref="IHubContext{ChatHub}"/> aos grupos <c>user_{id}</c> dos
/// dois membros (convenção do projeto). Rotas: <c>GET me/conversations</c>, <c>GET me/conversations/unread-count</c>,
/// <c>GET me/conversations/with/{userId}</c>, <c>POST conversations</c>, <c>GET conversations/{id}</c>,
/// <c>GET conversations/{id}/messages?before=&amp;limit=</c>, <c>POST conversations/{id}/messages</c>,
/// <c>POST conversations/{id}/read</c>, <c>DELETE messages/{id}</c>, <c>POST reports</c>.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public class ChatController : ControllerBase
{
    private const string NotFoundMessage = "Usuário não encontrado.";

    private readonly ChatService _chat;
    private readonly ReportService _reports;
    private readonly IHubContext<ChatHub> _hub;
    private readonly ILogger<ChatController> _logger;

    public ChatController(ChatService chat, ReportService reports, IHubContext<ChatHub> hub, ILogger<ChatController> logger)
    {
        _chat = chat;
        _reports = reports;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>As conversas da conta, da mais recente, com a última mensagem e as não lidas (sem as de contas bloqueadas).</summary>
    [HttpGet("me/conversations")]
    [ProducesResponseType(typeof(List<ConversationResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ConversationResponse>>> List(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _chat.ListAsync(userId, ct));
    }

    /// <summary>Quantas conversas têm mensagem não lida (o envelope do cabeçalho e o título da aba).</summary>
    [HttpGet("me/conversations/unread-count")]
    [ProducesResponseType(typeof(UnreadConversationsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadConversationsResponse>> UnreadCount(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(new UnreadConversationsResponse { Count = await _chat.CountUnreadConversationsAsync(userId, ct) });
    }

    /// <summary>Pode abrir conversa direta com a conta (e a conversa, se já existe)? Sem criar nada — o botão do perfil.</summary>
    [HttpGet("me/conversations/with/{userId:guid}")]
    [ProducesResponseType(typeof(CanMessageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CanMessageResponse>> CanMessage(Guid userId, CancellationToken ct)
    {
        if (UserId is not { } me)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _chat.CanOpenDirectAsync(me, userId, ct));
    }

    /// <summary>
    /// Abre (ou devolve) a conversa: <c>{ userId }</c> direta com a conta — amigos, ou organizador liberado por
    /// <c>MessagesFrom</c> (400 com a mensagem quando a regra não deixa; 404 para conta inexistente ou bloqueio) — ou
    /// <c>{ tournamentRequestId }</c> do pedido de vaga (SPEC-20261108-1639; 404 sem pedido em aberto). 429 acima de 20
    /// conversas novas no dia.
    /// </summary>
    [HttpPost("conversations")]
    [ProducesResponseType(typeof(OpenConversationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OpenConversationResponse>> Open([FromBody] OpenConversationRequest request, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        if (request.UserId is { } otherId)
        {
            var opened = await _chat.OpenDirectAsync(userId, otherId, ct);
            return opened is null ? NotFound(new { message = PlayerProfileService.ProfileNotFoundMessage }) : Ok(opened);
        }
        if (request.TournamentRequestId is { } requestId)
        {
            var opened = await _chat.OpenByRequestIdAsync(requestId, userId, ct);
            return opened is null ? NotFound(new { message = ChatService.RequestNotFoundMessage }) : Ok(opened);
        }
        throw new DomainException(ChatService.OpenNeedsTargetMessage);
    }

    /// <summary>Uma conversa da conta (cabeçalho da tela: a outra pessoa, lida até, não lidas). 404 se não é dela ou há bloqueio.</summary>
    [HttpGet("conversations/{id:guid}")]
    [ProducesResponseType(typeof(ConversationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationResponse>> Get(Guid id, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var conversation = await _chat.GetAsync(userId, id, ct);
        return conversation is null ? NotFound(new { message = ChatService.ConversationNotFoundMessage }) : Ok(conversation);
    }

    /// <summary>Histórico paginado: as <c>limit</c> (padrão 50, máx. 100) mais recentes antes de <c>before</c>, em ordem cronológica.</summary>
    [HttpGet("conversations/{id:guid}/messages")]
    [ProducesResponseType(typeof(MessagePageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessagePageResponse>> Messages(
        Guid id, [FromQuery] DateTimeOffset? before, [FromQuery] int limit = ChatService.DefaultPageSize, CancellationToken ct = default)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var page = await _chat.ListMessagesAsync(userId, id, before, limit, ct);
        return page is null ? NotFound(new { message = ChatService.ConversationNotFoundMessage }) : Ok(page);
    }

    /// <summary>
    /// Envia uma mensagem (texto puro, até 2.000). 404 se a conversa não é da conta ou há bloqueio; 429 acima de 60 por
    /// hora. Depois de gravar, <c>MessageReceived</c> vai aos dois membros (com as conversas não lidas de cada um).
    /// </summary>
    [HttpPost("conversations/{id:guid}/messages")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<MessageResponse>> Send(Guid id, [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var result = await _chat.SendAsync(userId, id, request.Body, ct);
        if (result is null)
            return NotFound(new { message = ChatService.ConversationNotFoundMessage });

        foreach (var memberId in new[] { result.RecipientId, result.SenderId })
        {
            var unread = await _chat.CountUnreadConversationsAsync(memberId, ct);
            await BroadcastAsync(memberId, ChatEvents.MessageReceived,
                new { conversationId = id, message = result.Message, unreadConversations = unread }, ct);
        }
        return Ok(result.Message);
    }

    /// <summary>Marca a conversa como lida até agora. <c>MessageRead</c> vai à outra conta (o "lida" dela) e às outras abas da própria.</summary>
    [HttpPost("conversations/{id:guid}/read")]
    [ProducesResponseType(typeof(ConversationReadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationReadResponse>> MarkRead(Guid id, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var result = await _chat.MarkReadAsync(userId, id, ct);
        if (result is null)
            return NotFound(new { message = ChatService.ConversationNotFoundMessage });

        // unreadConversations é de QUEM RECEBE o evento (o envelope de cada um), não de quem leu
        var unread = await _chat.CountUnreadConversationsAsync(userId, ct);
        var otherUnread = await _chat.CountUnreadConversationsAsync(result.OtherUserId, ct);
        await BroadcastAsync(result.OtherUserId, ChatEvents.MessageRead,
            new { conversationId = id, userId, readAt = result.ReadAt, unreadConversations = otherUnread }, ct);
        await BroadcastAsync(userId, ChatEvents.MessageRead,
            new { conversationId = id, userId, readAt = result.ReadAt, unreadConversations = unread }, ct);
        return Ok(new ConversationReadResponse { ReadAt = result.ReadAt, UnreadConversations = unread });
    }

    /// <summary>Apaga a própria mensagem (fica "mensagem apagada" para os dois). 404 se não existe ou não é desta conta.</summary>
    [HttpDelete("messages/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMessage(Guid id, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var result = await _chat.DeleteMessageAsync(userId, id, ct);
        if (result is null)
            return NotFound(new { message = ChatService.MessageNotFoundMessage });

        var payload = new { conversationId = result.ConversationId, messageId = result.MessageId };
        await BroadcastAsync(result.OtherUserId, ChatEvents.MessageDeleted, payload, ct);
        await BroadcastAsync(userId, ChatEvents.MessageDeleted, payload, ct);
        return NoContent();
    }

    /// <summary>
    /// Denuncia uma mensagem (<c>messageId</c>) ou uma conversa (<c>conversationId</c>) com um motivo da lista
    /// (Spam | Harassment | Scam | Inappropriate | Other) e detalhes opcionais. Guarda a cópia do texto; ninguém é avisado.
    /// </summary>
    [HttpPost("reports")]
    [ProducesResponseType(typeof(ReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportResponse>> Report([FromBody] CreateReportRequest request, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var result = await _reports.CreateAsync(userId, request, ct);
        return result is null ? NotFound(new { message = ChatService.ConversationNotFoundMessage }) : Ok(result);
    }

    /// <summary>Difunde ao grupo <c>user_{id}</c>; falha no envio só vai para o log (a gravação já valeu).</summary>
    private async Task BroadcastAsync(Guid userId, string eventName, object payload, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.Group(NotificationGroups.ForUser(userId)).SendAsync(eventName, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao difundir {Event} para a conta {UserId}", eventName, userId);
        }
    }

    private Guid? UserId
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            return Guid.TryParse(claim?.Value, out var id) ? id : null;
        }
    }
}
