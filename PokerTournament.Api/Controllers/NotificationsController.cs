using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Notificações da conta logada (SPEC-20261108-1635): o sino do cabeçalho e as preferências do Meu perfil. Tudo da
/// PRÓPRIA conta (o id vem do token). Sem [AllowTokenKind]: o token de dealer (QR) não chega aqui (403).
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private const string NotFoundMessage = "Usuário não encontrado.";
    private const string NotificationNotFoundMessage = "Notificação não encontrada.";

    private readonly NotificationService _notifications;

    public NotificationsController(NotificationService notifications)
    {
        _notifications = notifications;
    }

    /// <summary>As 20 últimas notificações da conta (mais recente primeiro) e quantas não lidas.</summary>
    [HttpGet("notifications")]
    [ProducesResponseType(typeof(NotificationListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationListResponse>> List(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _notifications.ListAsync(userId, ct));
    }

    /// <summary>Marca uma notificação como lida (idempotente). De outra conta ou inexistente → 404.</summary>
    [HttpPost("notifications/{id:guid}/read")]
    [ProducesResponseType(typeof(NotificationReadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationReadResponse>> MarkRead(Guid id, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var result = await _notifications.MarkReadAsync(userId, id, ct);
        return result is null ? NotFound(new { message = NotificationNotFoundMessage }) : Ok(result);
    }

    /// <summary>Marca todas as não lidas da conta: <c>{ unreadCount: 0, marked }</c>.</summary>
    [HttpPost("notifications/read-all")]
    [ProducesResponseType(typeof(NotificationReadResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationReadResponse>> MarkAllRead(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _notifications.MarkAllReadAsync(userId, ct));
    }

    /// <summary>Preferências: e-mail resumo e as categorias (torneio, pedidos e avisos, social).</summary>
    [HttpGet("notification-preferences")]
    [ProducesResponseType(typeof(NotificationPreferencesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationPreferencesResponse>> GetPreferences(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var prefs = await _notifications.GetPreferencesAsync(userId, ct);
        return prefs is null ? NotFound(new { message = NotFoundMessage }) : Ok(prefs);
    }

    /// <summary>Salva as preferências (campo ausente não mexe) e devolve como ficaram.</summary>
    [HttpPut("notification-preferences")]
    [ProducesResponseType(typeof(NotificationPreferencesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationPreferencesResponse>> UpdatePreferences(
        [FromBody] UpdateNotificationPreferencesRequest request, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var prefs = await _notifications.UpdatePreferencesAsync(userId, request, ct);
        return prefs is null ? NotFound(new { message = NotFoundMessage }) : Ok(prefs);
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
