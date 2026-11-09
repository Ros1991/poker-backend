using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Amigos, pedidos e bloqueios (SPEC-20261108-1637): qualquer conta logada (qualquer papel). Sem escopo de home game e
/// fora do filtro de tenant (nenhuma rota tem homeGameId/tournamentId). Sem [AllowTokenKind]: o token de dealer (QR)
/// não chega aqui (403). As regras estão no <see cref="FriendshipService"/>. Rotas:
/// <c>GET me/friends</c>, <c>GET me/friend-requests</c>, <c>GET me/blocks</c>, <c>POST players/{handle}/friend-request</c>,
/// <c>POST friend-requests/{id}/accept|decline</c>, <c>DELETE friend-requests/{id}</c>, <c>DELETE me/friends/{userId}</c>,
/// <c>POST/DELETE players/{handle}/block</c>.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public class FriendsController : ControllerBase
{
    private const string NotFoundMessage = "Usuário não encontrado.";

    private readonly FriendshipService _friends;

    public FriendsController(FriendshipService friends)
    {
        _friends = friends;
    }

    /// <summary>Os amigos da conta, com "jogando agora" nos torneios em andamento que ela pode acompanhar.</summary>
    [HttpGet("me/friends")]
    [ProducesResponseType(typeof(List<FriendResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FriendResponse>>> GetFriends(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _friends.ListFriendsAsync(userId, ct));
    }

    /// <summary>Pedidos pendentes: recebidos (aceitar/recusar) e enviados (cancelar). Recusados não aparecem para ninguém.</summary>
    [HttpGet("me/friend-requests")]
    [ProducesResponseType(typeof(FriendRequestsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FriendRequestsResponse>> GetRequests(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _friends.ListRequestsAsync(userId, ct));
    }

    /// <summary>As contas que eu bloqueei (seção Privacidade do Meu perfil), com o handle para desbloquear.</summary>
    [HttpGet("me/blocks")]
    [ProducesResponseType(typeof(List<BlockedUserResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<BlockedUserResponse>>> GetBlocks(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _friends.ListBlocksAsync(userId, ct));
    }

    /// <summary>
    /// Pede amizade a <c>{handle}</c>. 400 com mensagem clara (própria conta, já amigos, já pendente, privacidade de quem
    /// recebe, carência de 30 dias, mais de 20 pendentes); 404 para handle inexistente ou bloqueio em qualquer direção.
    /// Se a outra conta já tinha pedido, vira amizade (<c>becameFriends</c>).
    /// </summary>
    [HttpPost("players/{handle}/friend-request")]
    [ProducesResponseType(typeof(FriendshipActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FriendshipActionResponse>> RequestFriendship(string handle, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var result = await _friends.RequestAsync(userId, handle, ct);
        return result is null ? NotFound(new { message = PlayerProfileService.ProfileNotFoundMessage }) : Ok(result);
    }

    /// <summary>Aceita um pedido recebido (só quem recebeu). 404 se não existe, já foi respondido ou não é desta conta.</summary>
    [HttpPost("friend-requests/{id:guid}/accept")]
    [ProducesResponseType(typeof(FriendshipActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FriendshipActionResponse>> Accept(Guid id, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var result = await _friends.AcceptAsync(userId, id, ct);
        return result is null ? NotFound(new { message = FriendshipService.RequestNotFoundMessage }) : Ok(result);
    }

    /// <summary>Recusa um pedido recebido (só quem recebeu). Quem pediu não é avisado. 404 se não existe ou não é desta conta.</summary>
    [HttpPost("friend-requests/{id:guid}/decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Decline(Guid id, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return await _friends.DeclineAsync(userId, id, ct)
            ? NoContent()
            : NotFound(new { message = FriendshipService.RequestNotFoundMessage });
    }

    /// <summary>Quem pediu cancela o pedido pendente. 404 se não existe ou não é desta conta.</summary>
    [HttpDelete("friend-requests/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return await _friends.CancelRequestAsync(userId, id, ct)
            ? NoContent()
            : NotFound(new { message = FriendshipService.RequestNotFoundMessage });
    }

    /// <summary>Desfaz a amizade com a conta <c>{userId}</c>. 404 se não são amigos.</summary>
    [HttpDelete("me/friends/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFriend(Guid userId, CancellationToken ct)
    {
        if (UserId is not { } me)
            return NotFound(new { message = NotFoundMessage });
        return await _friends.RemoveFriendAsync(me, userId, ct)
            ? NoContent()
            : NotFound(new { message = FriendshipService.FriendNotFoundMessage });
    }

    /// <summary>
    /// Bloqueia <c>{handle}</c>: apaga amizade ou pedido entre os dois; o perfil some nos dois sentidos, ele sai do
    /// "jogou com você" e ninguém pede amizade. Idempotente. 404 para handle inexistente ou quando ELE já bloqueou você.
    /// </summary>
    [HttpPost("players/{handle}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Block(string handle, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var result = await _friends.BlockAsync(userId, handle, ct);
        return result is null ? NotFound(new { message = PlayerProfileService.ProfileNotFoundMessage }) : NoContent();
    }

    /// <summary>
    /// Desbloqueia <c>{handle}</c> (só quem bloqueou). 404 para handle inexistente/inativo E quando não há bloqueio seu
    /// sobre a conta (revisão da onda 2, M4: a resposta não diz se a conta existe nem se ela bloqueou você).
    /// </summary>
    [HttpDelete("players/{handle}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unblock(string handle, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var result = await _friends.UnblockAsync(userId, handle, ct);
        return result is null ? NotFound(new { message = PlayerProfileService.ProfileNotFoundMessage }) : NoContent();
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
