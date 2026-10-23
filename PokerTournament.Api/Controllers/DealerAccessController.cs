using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PokerTournament.Api.Authorization;
using PokerTournament.Api.Hubs;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// QR do dealer com autorização do organizador.
/// - Gerar/ver/revogar o QR e listar/autorizar/recusar/desconectar pedidos: quem administra
///   o torneio (o {tournamentId} na rota passa pelo filtro de tenant; token de dealer não
///   chega aqui — sem [AllowTokenKind], o dealer não autoriza a si mesmo).
/// - Pedir acesso e consultar o pedido: anônimo (o aparelho do dealer), com limite por IP
///   na criação. O token só sai no GET do pedido aprovado, para o aparelho que o criou.
/// - Desconectar um dealer / desativar o QR também derruba a conexão do hub já aberta
///   (DealerConnectionRegistry): sai do grupo do torneio, recebe "DealerRevoked" e cai.
/// </summary>
[ApiController]
[Authorize]
public class DealerAccessController : ControllerBase
{
    private readonly DealerAccessService _service;
    private readonly DealerConnectionRegistry _dealerConnections;

    public DealerAccessController(DealerAccessService service, DealerConnectionRegistry dealerConnections)
    {
        _service = service;
        _dealerConnections = dealerConnections;
    }

    // ---------------------------------------------------------------- QR (organizador)

    [HttpPost("api/v1/tournaments/{tournamentId:guid}/dealer-access")]
    public async Task<ActionResult<DealerAccessResponse>> Create(Guid tournamentId, CancellationToken ct)
    {
        return Ok(await _service.CreateAsync(tournamentId, GetUserId(), ct));
    }

    [HttpGet("api/v1/tournaments/{tournamentId:guid}/dealer-access")]
    public async Task<ActionResult<DealerAccessResponse>> GetActive(Guid tournamentId, CancellationToken ct)
    {
        var access = await _service.GetActiveAsync(tournamentId, ct);
        return access is null ? NoContent() : Ok(access);
    }

    /// <summary>Revoga o QR e derruba todos os dealers aprovados por ele.</summary>
    [HttpDelete("api/v1/tournaments/{tournamentId:guid}/dealer-access")]
    public async Task<ActionResult> Revoke(Guid tournamentId, CancellationToken ct)
    {
        await _service.RevokeAsync(tournamentId, GetUserId(), ct);
        // Depois do commit (o serviço já marcou os pedidos inativos no cache): nenhum
        // dealer do torneio continua valendo, então caem todas as conexões de dealer dele
        await _dealerConnections.DisconnectTournamentAsync(tournamentId);
        return NoContent();
    }

    // ------------------------------------------------------- pedido (aparelho, anônimo)

    /// <summary>O aparelho leu o QR e informou o nome: cria (ou reaproveita) o pedido.</summary>
    [HttpPost("api/v1/dealer-access/requests")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    public async Task<ActionResult<DealerRequestCreatedResponse>> CreateRequest(
        [FromBody] DealerRequestCreateRequest request, CancellationToken ct)
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        return Ok(await _service.RequestAsync(request.Code, request.Name, request.DeviceKey, userAgent, ct));
    }

    /// <summary>
    /// O aparelho consulta o pedido a cada poucos segundos. O token só vem quando o pedido
    /// está Approved, no prazo, e o deviceKey é o mesmo que criou o pedido.
    /// </summary>
    [HttpGet("api/v1/dealer-access/requests/{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<DealerRequestStatusResponse>> GetRequest(
        Guid id, [FromQuery] string? deviceKey, CancellationToken ct)
    {
        var status = await _service.GetStatusAsync(id, deviceKey, ct);
        return status is null
            ? NotFound(new { message = "Pedido não encontrado. Leia o QR de novo." })
            : Ok(status);
    }

    // ---------------------------------------------------------------- organizador

    /// <summary>Pedidos pendentes e dealers conectados do torneio.</summary>
    [HttpGet("api/v1/tournaments/{tournamentId:guid}/dealer-requests")]
    public async Task<ActionResult<List<DealerRequestResponse>>> ListRequests(Guid tournamentId, CancellationToken ct)
    {
        return Ok(await _service.ListAsync(tournamentId, ct));
    }

    [HttpPost("api/v1/tournaments/{tournamentId:guid}/dealer-requests/{id:guid}/approve")]
    public async Task<ActionResult<DealerRequestResponse>> Approve(Guid tournamentId, Guid id, CancellationToken ct)
    {
        return Ok(await _service.ApproveAsync(tournamentId, id, GetUserId(), ct));
    }

    [HttpPost("api/v1/tournaments/{tournamentId:guid}/dealer-requests/{id:guid}/reject")]
    public async Task<ActionResult<DealerRequestResponse>> Reject(Guid tournamentId, Guid id, CancellationToken ct)
    {
        return Ok(await _service.RejectAsync(tournamentId, id, GetUserId(), ct));
    }

    /// <summary>
    /// Desconecta um dealer aprovado: só aquele aparelho cai — as requisições na hora (cache
    /// marcado inativo) e a conexão do hub já aberta logo depois do aviso "DealerRevoked".
    /// </summary>
    [HttpPost("api/v1/tournaments/{tournamentId:guid}/dealer-requests/{id:guid}/revoke")]
    public async Task<ActionResult<DealerRequestResponse>> RevokeRequest(Guid tournamentId, Guid id, CancellationToken ct)
    {
        var result = await _service.RevokeRequestAsync(tournamentId, id, GetUserId(), ct);
        await _dealerConnections.DisconnectRequestAsync(id);
        return Ok(result);
    }

    // ------------------------------------------------------------------- terminal

    /// <summary>O terminal confere a sessão do dealer ao abrir (no lugar do /auth/me).</summary>
    [HttpGet("api/v1/tournaments/{tournamentId:guid}/dealer-access/session")]
    [AllowTokenKind(TokenKinds.Dealer)]
    public async Task<ActionResult<DealerSessionResponse>> Session(Guid tournamentId, CancellationToken ct)
    {
        if (User.FindFirst(TokenKinds.ClaimType)?.Value != TokenKinds.Dealer
            || GetUserId() is not { } requestId)
            return NotFound(new { message = "Sessão de dealer não encontrada." });

        var session = await _service.GetSessionAsync(requestId, ct);
        return session is null ? NotFound(new { message = "Sessão de dealer não encontrada." }) : Ok(session);
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out var id) ? id : null;
    }
}
