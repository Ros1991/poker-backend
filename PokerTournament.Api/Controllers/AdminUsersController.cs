using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Api.Http;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Administração geral (SPEC-20261023-1206): só o Admin do sistema (policy "Admin" — o token de dealer nunca chega
/// aqui). Fino: as regras ficam no <see cref="AdminUserService"/>. Não existe criar conta pela administração (D14).
/// Toda ação grava AuditLog com o Admin, o IP (último hop do X-Forwarded-For) e o User-Agent.
/// </summary>
[ApiController]
[Route("api/v1/admin/users")]
[Authorize(Policy = "Admin")]
public class AdminUsersController : ControllerBase
{
    private readonly AdminUserService _admin;
    private readonly IMapper _mapper;

    public AdminUsersController(AdminUserService admin, IMapper mapper)
    {
        _admin = admin;
        _mapper = mapper;
    }

    /// <summary>
    /// Contas com busca (nome, apelido, e-mail), filtros por plano efetivo (Mesa | Clube | Liga), status
    /// (active | inactive | all — padrão all) e <c>requested=true</c> (pediu plano nas boas-vindas e ainda não foi
    /// atendido). 20 por página (máx. 100).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminUserListItemResponse>>> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? plan = null,
        [FromQuery] string? status = null,
        [FromQuery] bool requested = false,
        CancellationToken ct = default)
    {
        return Ok(await _admin.ListAsync(search, page, pageSize, plan, status, requested, ct));
    }

    /// <summary>Ficha: dados, acesso e sessões, e-mail e formas de entrar, home games, plano e históricos.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminUserDetailResponse>> Get(Guid id, CancellationToken ct)
    {
        var detail = await _admin.GetDetailAsync(id, ct);
        return detail is null ? UserNotFound() : Ok(detail);
    }

    /// <summary>Editar dados e papel (sem senha). E-mail de outra conta → 409; Admin se rebaixando ou último Admin → 400.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AdminUserDetailResponse>> Update(
        Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        var detail = await _admin.UpdateAsync(id, request, Actor, ct);
        return detail is null ? UserNotFound() : Ok(detail);
    }

    /// <summary>
    /// Alterar plano: plano, vencimento (opcional, no futuro), origem e observação. Grava AuditLog PlanChange e vale na
    /// hora (o <c>GET me/plan</c> da conta já devolve o plano novo). Devolve a ficha atualizada.
    /// </summary>
    [HttpPut("{id:guid}/plan")]
    public async Task<ActionResult<AdminUserDetailResponse>> ChangePlan(
        Guid id, [FromBody] ChangeUserPlanRequest request, CancellationToken ct)
    {
        var user = await _admin.ChangePlanAsync(id, request, Actor, requireFutureExpiry: true, ct);
        if (user is null)
            return UserNotFound();
        return Ok(await _admin.GetDetailAsync(id, ct));
    }

    /// <summary>
    /// Alias da SPEC-20261023-1205 (<c>PUT api/v1/users/{id}/plan</c>, Admin): o mesmo caminho de gravação, com o
    /// <see cref="UserResponse"/> de antes e aceitando vencimento no passado (simular plano vencido). O site usa
    /// <c>PUT admin/users/{id}/plan</c>.
    /// </summary>
    [HttpPut("~/api/v1/users/{id:guid}/plan")]
    public async Task<ActionResult<UserResponse>> ChangePlanLegacy(
        Guid id, [FromBody] ChangeUserPlanRequest request, CancellationToken ct)
    {
        var user = await _admin.ChangePlanAsync(id, request, Actor, requireFutureExpiry: false, ct);
        return user is null ? UserNotFound() : Ok(_mapper.Map<UserResponse>(user));
    }

    /// <summary>
    /// Redefinir senha: <c>Temporary</c> (senha definida aqui; todas as sessões caem), <c>Email</c> (409 sem envio de
    /// e-mail configurado) ou <c>Link</c> (24 h, uso único, para mandar por WhatsApp).
    /// </summary>
    [HttpPost("{id:guid}/password")]
    public async Task<ActionResult<AdminResetPasswordResponse>> ResetPassword(
        Guid id, [FromBody] AdminResetPasswordRequest request, CancellationToken ct)
    {
        var result = await _admin.ResetPasswordAsync(id, request, Actor, ct);
        return result is null ? UserNotFound() : Ok(result);
    }

    /// <summary>Ativar/desativar (desativar derruba todas as sessões). Devolve a ficha atualizada.</summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<AdminUserDetailResponse>> SetStatus(
        Guid id, [FromBody] ChangeUserStatusRequest request, CancellationToken ct)
    {
        var detail = await _admin.SetStatusAsync(id, request.IsActive, Actor, ct);
        return detail is null ? UserNotFound() : Ok(detail);
    }

    /// <summary>Confirmar o e-mail manualmente (D16.7). Devolve a ficha atualizada.</summary>
    [HttpPost("{id:guid}/confirm-email")]
    public async Task<ActionResult<AdminUserDetailResponse>> ConfirmEmail(Guid id, CancellationToken ct)
    {
        var detail = await _admin.ConfirmEmailAsync(id, Actor, ct);
        return detail is null ? UserNotFound() : Ok(detail);
    }

    /// <summary>Reenviar o e-mail de confirmação (409 sem envio de e-mail configurado).</summary>
    [HttpPost("{id:guid}/confirmation-email")]
    public async Task<ActionResult<AdminEmailSentResponse>> ResendConfirmation(Guid id, CancellationToken ct)
    {
        var result = await _admin.ResendConfirmationAsync(id, Actor, ct);
        return result is null ? UserNotFound() : Ok(result);
    }

    private NotFoundObjectResult UserNotFound() => NotFound(new { message = AdminUserService.UserNotFoundMessage });

    private AdminActor Actor => new(
        Guid.TryParse((User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub"))?.Value, out var id) ? id : Guid.Empty,
        ClientIp.Resolve(HttpContext),
        Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null);
}
