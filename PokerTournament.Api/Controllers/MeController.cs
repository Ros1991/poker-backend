using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PokerTournament.Api.Http;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Dados da conta logada. Sem [AllowTokenKind]: o token de dealer (QR) não chega aqui (403).
/// "Meu perfil" (SPEC-20261023-1211): dados, foto, senha, contas conectadas, aparelhos e excluir conta — tudo da
/// PRÓPRIA conta (o id vem do token). As regras ficam no AuthService (AuthService.Profile.cs); cada ação grava AuditLog.
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MeController : ControllerBase
{
    private const string NotFoundMessage = "Usuário não encontrado.";

    private readonly PlanService _plans;
    private readonly AuthService _auth;
    private readonly PasswordResetService _passwordReset;
    private readonly PhotoService _photos;
    private readonly UploadsOptions _uploads;
    private readonly AccountEmailService _accountEmails;
    private readonly ILogger<MeController> _logger;

    public MeController(
        PlanService plans,
        AuthService auth,
        PasswordResetService passwordReset,
        PhotoService photos,
        UploadsOptions uploads,
        AccountEmailService accountEmails,
        ILogger<MeController> logger)
    {
        _plans = plans;
        _auth = auth;
        _passwordReset = passwordReset;
        _photos = photos;
        _uploads = uploads;
        _accountEmails = accountEmails;
        _logger = logger;
    }

    /// <summary>
    /// Plano da conta (SPEC-20261023-1205): plano gravado e efetivo, vencimento, origem, nota, limites e
    /// recursos do plano efetivo, uso (home games n/limite, torneios nos últimos 7 dias n/limite + quando
    /// libera o próximo, membros por home game) e o catálogo inteiro (tabela comparativa de "Meu plano"). Desde a
    /// SPEC-20261029-1723 traz o <c>primaryHomeGameId</c> (home game principal da conta).
    /// </summary>
    [HttpGet("plan")]
    public async Task<ActionResult<MyPlanResponse>> GetPlan(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });

        var plan = await _plans.GetMyPlanAsync(userId, ct);
        return plan is null ? NotFound(new { message = NotFoundMessage }) : Ok(plan);
    }

    // ---------------------------------------------------------------- dados e foto

    /// <summary>Perfil da conta: nome, apelido, WhatsApp (55 + DDD + número), foto, e-mail (só leitura), tem senha?,
    /// quantos home games é dona (bloqueia excluir), o plano efetivo e — boas-vindas (SPEC-20261023-1212) —
    /// <c>needsOnboarding</c>, a foto veio do Google? e o plano pago pedido.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProfileResponse>> GetProfile(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var profile = await _auth.GetProfileAsync(userId, ct);
        return profile is null ? NotFound(new { message = NotFoundMessage }) : Ok(profile);
    }

    /// <summary>Salva nome (obrigatório), apelido e WhatsApp. Validação → 400 com a mensagem.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfileResponse>> UpdateProfile(
        [FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _auth.UpdateProfileAsync(userId, request, Ip, UserAgent, ct));
    }

    /// <summary>Foto da conta (campo <c>file</c>, JPEG/PNG/WebP até 2 MB — o site manda o recorte quadrado 512 × 512).</summary>
    [HttpPost("photo")]
    [RequestSizeLimit(ImageFiles.MaxRequestBytes)]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfileResponse>> UploadPhoto(IFormFile? file, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        if (file is null || file.Length == 0)
            return BadRequest(new { message = ImageFiles.InvalidPhotoMessage });

        await using var stream = file.OpenReadStream();
        var (profile, previous) = await _auth.SetPhotoAsync(userId, _photos, stream, file.Length, Ip, UserAgent, ct);
        DeleteLocalPhoto(previous);
        return Ok(profile);
    }

    /// <summary>Tira a foto da conta (volta às iniciais).</summary>
    [HttpDelete("photo")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProfileResponse>> RemovePhoto(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var (profile, previous) = await _auth.RemovePhotoAsync(userId, Ip, UserAgent, ct);
        DeleteLocalPhoto(previous);
        return Ok(profile);
    }

    // ---------------------------------------------------------------- boas-vindas (SPEC-20261023-1212)

    /// <summary>
    /// Conclui as boas-vindas do primeiro acesso: <c>{ nickname?, whatsapp?, useProviderPhoto?, plan }</c>. Campo
    /// ausente = não mexe ("Fazer depois" manda só o plano). <c>plan</c> Mesa começa na hora; Clube/Liga só gravam o
    /// pedido (o plano muda pelo Admin) e o site abre o WhatsApp do suporte. Devolve o perfil com
    /// <c>needsOnboarding = false</c>. Validação → 400 com a mensagem.
    /// </summary>
    [HttpPost("onboarding")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfileResponse>> CompleteOnboarding(
        [FromBody] OnboardingRequest request, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _auth.CompleteOnboardingAsync(userId, request, Ip, UserAgent, ct));
    }

    // ---------------------------------------------------------------- senha

    /// <summary>
    /// Trocar senha: <c>{ currentPassword, newPassword }</c>. Atual errada → 400 "Senha atual incorreta." (conta no
    /// limite de senhas erradas do login → 429); conta sem senha → 400 (use <c>password/setup-link</c>). Os outros
    /// aparelhos saem; este continua.
    /// </summary>
    [HttpPost("password")]
    [ProducesResponseType(typeof(ChangePasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChangePasswordResponse>> ChangePassword(
        [FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _auth.ChangePasswordAsync(userId, AuthService.GetSessionId(User), request, Ip, UserAgent, ct));
    }

    /// <summary>
    /// Definir senha (conta que só entra pelo Google): 202 e o link "Definir sua senha" vai para o e-mail da conta — o
    /// mesmo link de uso único (1 h) do "Esqueci minha senha" (SPEC-20261023-1203). Conta com senha → 400; outro
    /// pedido em menos de 2 min → 429 <c>too_many_requests</c>.
    /// </summary>
    [HttpPost("password/setup-link")]
    [ProducesResponseType(typeof(PasswordSetupLinkResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PasswordSetupLinkResponse>> PasswordSetupLink(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });

        var email = await _auth.PreparePasswordSetupAsync(userId, Ip, UserAgent, ct);
        var (_, link) = await _passwordReset.IssueTokenAsync(userId, Ip, UserAgent, ct);
        _accountEmails.QueuePasswordSetup(email, link);

        var masked = PasswordResetEmail.Mask(email);
        return Accepted(new PasswordSetupLinkResponse
        {
            Email = masked,
            Message = $"Enviamos um link para {masked}. Ele vale por 1 hora; confira também a caixa de spam.",
        });
    }

    // ---------------------------------------------------------------- contas conectadas

    /// <summary>Formas de entrar: tem senha? e, por provedor, conectado/conta/pode desconectar.</summary>
    [HttpGet("logins")]
    [ProducesResponseType(typeof(ExternalLoginsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExternalLoginsResponse>> GetLogins(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _auth.GetExternalLoginsAsync(userId, ct));
    }

    /// <summary>
    /// Conectar o Google pelo perfil: <c>{ idToken, nonce }</c> (nonce de <c>POST auth/external/nonce</c>). Token ou
    /// nonce inválido → 400; conta Google já ligada a outra conta → 400; provedor desligado → 404.
    /// </summary>
    [HttpPost("logins/{provider}")]
    [ProducesResponseType(typeof(ExternalLoginsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExternalLoginsResponse>> ConnectLogin(
        string provider, [FromBody] ConnectExternalLoginRequest request, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _auth.ConnectExternalLoginAsync(userId, provider, request, Ip, UserAgent, ct));
    }

    /// <summary>Desconectar o provedor. Única forma de entrar (sem senha e sem outro login) → 400.</summary>
    [HttpDelete("logins/{provider}")]
    [ProducesResponseType(typeof(ExternalLoginsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExternalLoginsResponse>> DisconnectLogin(string provider, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _auth.DisconnectExternalLoginAsync(userId, provider, Ip, UserAgent, ct));
    }

    // ---------------------------------------------------------------- aparelhos

    /// <summary>Aparelhos conectados (sessões ativas): nome do navegador/sistema, criação, último uso e "este aparelho".</summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(List<SessionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SessionResponse>>> GetSessions(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _auth.GetSessionsAsync(userId, AuthService.GetSessionId(User), ct));
    }

    /// <summary>Tira um aparelho da conta. Sessão de outra conta, inexistente ou já encerrada → 404.</summary>
    [HttpDelete("sessions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var revoked = await _auth.RevokeSessionAsync(userId, id, AuthService.GetSessionId(User), Ip, UserAgent, ct);
        return revoked ? NoContent() : NotFound(new { message = "Aparelho não encontrado ou já desconectado." });
    }

    /// <summary>Sair de todos os outros aparelhos (este continua): <c>{ revoked }</c>.</summary>
    [HttpPost("sessions/revoke-others")]
    [ProducesResponseType(typeof(RevokeSessionsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<RevokeSessionsResponse>> RevokeOtherSessions(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var revoked = await _auth.RevokeOtherSessionsAsync(userId, AuthService.GetSessionId(User), Ip, UserAgent, ct);
        return Ok(new RevokeSessionsResponse { Revoked = revoked });
    }

    // ---------------------------------------------------------------- excluir conta

    /// <summary>
    /// Excluir a conta: <c>{ confirmation: "EXCLUIR", password }</c> ou, sem senha, <c>{ confirmation, idToken, nonce }</c>
    /// de um login com o Google feito agora. Dono de home game além do principal da conta → 400 ("Você é dono de N home
    /// games além do da sua conta…"); torneio em andamento no principal → 400. 204 = conta anonimizada, sessões
    /// revogadas, e-mail liberado e o home game principal excluído junto (SPEC-20261029-1723).
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult> DeleteAccount(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeleteAccountRequest? request, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var previous = await _auth.DeleteAccountAsync(userId, request ?? new DeleteAccountRequest(), Ip, UserAgent, ct);
        DeleteLocalPhoto(previous);
        return NoContent();
    }

    // ---------------------------------------------------------------- apoio

    private Guid? UserId
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            return Guid.TryParse(claim?.Value, out var id) ? id : null;
        }
    }

    private string? Ip => ClientIp.Resolve(HttpContext);

    private string? UserAgent => Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    /// <summary>Apaga o arquivo da foto anterior quando é nosso (/uploads/photos/...); URL externa (Google) fica.</summary>
    private void DeleteLocalPhoto(string? url)
    {
        var path = _uploads.LocalFileFor(url, UploadsOptions.PhotosFolder);
        if (path is null)
            return;
        try
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
            // Arquivo órfão não quebra a troca: só fica registrado
            _logger.LogWarning(ex, "Não consegui apagar a foto antiga da conta {Path}", path);
        }
    }
}
