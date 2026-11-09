using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Api.Http;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Denúncias na Administração (SPEC-20261108-1638, D11): só o Admin do sistema (policy "Admin" — o token de dealer nunca
/// chega aqui). Lista com o texto denunciado, quem denunciou e o denunciado (com o id para a ficha da conta) e marca como
/// revisada. Regras no <see cref="ReportService"/>.
/// </summary>
[ApiController]
[Route("api/v1/admin/reports")]
[Authorize(Policy = "Admin")]
public class AdminReportsController : ControllerBase
{
    private readonly ReportService _reports;

    public AdminReportsController(ReportService reports)
    {
        _reports = reports;
    }

    /// <summary>Denúncias por situação (<c>open</c> padrão, <c>reviewed</c> ou <c>all</c>), mais recentes primeiro, 20 por página (máx. 100).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<AdminReportResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginatedResponse<AdminReportResponse>>> List(
        [FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        return Ok(await _reports.ListAsync(status, page, pageSize, ct));
    }

    /// <summary>Marca a denúncia como revisada (idempotente; grava AuditLog). 404 se não existe.</summary>
    [HttpPost("{id:guid}/review")]
    [ProducesResponseType(typeof(AdminReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminReportResponse>> Review(Guid id, CancellationToken ct)
    {
        var adminId = Guid.TryParse((User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub"))?.Value, out var parsed)
            ? parsed
            : Guid.Empty;
        var result = await _reports.ReviewAsync(id, adminId, ClientIp.Resolve(HttpContext),
            Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null, ct);
        return result is null ? NotFound(new { message = ReportService.ReportNotFoundMessage }) : Ok(result);
    }
}
