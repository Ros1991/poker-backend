using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PokerTournament.Api.Http;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private const string ForgotPasswordMessage =
        "Se o e-mail estiver cadastrado, enviamos um link para redefinir a senha. Confira também a caixa de spam.";

    private const string ResendConfirmationMessage =
        "Se o e-mail estiver cadastrado e ainda não confirmado, enviamos um novo link.";

    private readonly AuthService _authService;
    private readonly PasswordResetService _passwordReset;
    private readonly EmailConfirmationService _emailConfirmation;

    public AuthController(
        AuthService authService, PasswordResetService passwordReset, EmailConfirmationService emailConfirmation)
    {
        _authService = authService;
        _passwordReset = passwordReset;
        _emailConfirmation = emailConfirmation;
    }

    /// <summary>
    /// Entrar. E-mail ou senha errados (ou conta que só entra pelo Google, sem senha) → 400 neutro. Senha certa
    /// com o e-mail ainda não confirmado → 403 <c>{ code: "email_not_confirmed", message }</c> (sem sessão).
    /// Com <c>linkTicket</c> (409 do login social) a senha certa também conecta o Google à conta; ticket
    /// vencido/usado/de outra conta → 400. Senha errada demais → 429 <c>{ code: "too_many_attempts", retryAfter,
    /// message }</c> + <c>Retry-After</c>, antes de conferir a senha: 10 por IP + e-mail, 50 por e-mail e 50 por IP a
    /// cada 15 min, contando só as erradas (LoginAttemptLimiter) — a mesa no mesmo Wi-Fi não se trava.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, UserAgent, ClientIp.Resolve(HttpContext), ct);
        return Ok(result);
    }

    /// <summary>
    /// Cadastro público: 202 com <c>{ email, message }</c> e SEM sessão — a conta só entra depois
    /// de confirmar o e-mail pelo link (48 h) com a senha do cadastro. E-mail de conta confirmada → 400 "E-mail já
    /// está em uso."; e-mail de conta ainda não confirmada → link novo com os dados novos, revogando os anteriores
    /// (202 igual; dentro do cooldown de 2 min o e-mail não sai). Anônimo e manda e-mail: limite por IP de
    /// 20/min (public-anon) + teto diário por IP → 429.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterAsync(request, ClientIp.Resolve(HttpContext), UserAgent, ct);
        return Accepted(result);
    }

    /// <summary>
    /// Confirma o e-mail pelo link {PUBLIC_APP_URL}/confirmar-email/{token} com a senha escolhida no cadastro
    /// (D16.10): 200 com a MESMA <c>AuthResponse</c> do login (já entra). Link inválido, expirado, já usado ou
    /// substituído por um mais novo → 400 "Link inválido ou expirado…"; senha errada → 400 "Senha incorreta." (o link
    /// continua valendo; conta no limite de senhas erradas do login → 429 <c>too_many_attempts</c>).
    /// </summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request, CancellationToken ct)
    {
        var result = await _authService.ConfirmEmailAsync(
            request.Token, request.Password, ClientIp.Resolve(HttpContext), UserAgent, ct);
        return Ok(result);
    }

    /// <summary>
    /// O que a página do link mostra antes de pedir a senha: <c>{ valid, email }</c> com o e-mail MASCARADO
    /// (ex.: "ro***@gmail.com"); link que não vale → <c>{ valid: false, email: null }</c>. Não consome o link.
    /// </summary>
    [HttpGet("confirm-email/{token}/info")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    [ProducesResponseType(typeof(ConfirmEmailInfoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ConfirmEmailInfoResponse>> ConfirmEmailInfo(string token, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await _emailConfirmation.GetInfoAsync(token, ct));
    }

    /// <summary>
    /// Novo link de confirmação: sempre 202 com a mesma mensagem — e-mail inexistente, já
    /// confirmado ou em cooldown (2 min) não se distinguem pela resposta. O link anterior deixa de valer.
    /// </summary>
    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult> ResendConfirmation(
        [FromBody] ResendConfirmationRequest request, CancellationToken ct)
    {
        await _emailConfirmation.ResendAsync(request.Email, ClientIp.Resolve(HttpContext), UserAgent, ct);
        return Accepted(new { message = ResendConfirmationMessage });
    }

    // ---- Login social (SPEC-20261023-1210) ----

    /// <summary>
    /// Provedores de login social ligados e o Client ID público de cada um: <c>{ providers: [{ provider, clientId }] }</c>.
    /// O site monta o botão oficial a partir daqui (sem variável nova no build). Apple desligada = fora da lista.
    /// </summary>
    [HttpGet("external/providers")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    [ProducesResponseType(typeof(ExternalProvidersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public ActionResult<ExternalProvidersResponse> ExternalProviders() => Ok(_authService.GetExternalProviders());

    /// <summary>
    /// Nonce de uso único (10 min) que o site passa ao botão do provedor antes do clique; volta dentro do ID token e
    /// é conferido e gasto em <c>POST auth/external/{provider}</c>. Assinado pela API e sem estado até ser gasto: pedir
    /// nonce em massa não ocupa memória nem recusa ninguém (só o limite por IP do public-anon).
    /// </summary>
    [HttpPost("external/nonce")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    [ProducesResponseType(typeof(ExternalNonceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public ActionResult<ExternalNonceResponse> ExternalNonce()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(_authService.IssueExternalNonce());
    }

    /// <summary>
    /// Entrar com o provedor (<c>google</c>; <c>apple</c> só quando ligada). Corpo <c>{ idToken, nonce, linkTicket? }</c>.
    /// 200 = a mesma <c>AuthResponse</c> do login por senha (sessão por aparelho). ID token inválido (assinatura,
    /// emissor, audience, validade) ou nonce ausente/reusado/vencido → 400 neutro; sem e-mail verificado → 400;
    /// conta desativada → 403 <c>{ code: "account_disabled" }</c>; conta confirmada que pede a senha → 409
    /// <c>{ code: "link_requires_password", ticket, email, message }</c>; provedor desligado → 404.
    /// </summary>
    [HttpPost("external/{provider}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> ExternalLogin(
        string provider, [FromBody] ExternalLoginRequest request, CancellationToken ct)
    {
        var result = await _authService.ExternalLoginAsync(
            provider, request, ClientIp.Resolve(HttpContext), UserAgent, ct);
        return Ok(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh(
        [FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await _authService.RefreshTokenAsync(request.RefreshToken, UserAgent, ct);
        return Ok(result);
    }

    /// <summary>
    /// "Esqueci minha senha": sempre 202 com a mesma mensagem — e-mail cadastrado, não
    /// cadastrado ou usuário inativo não se distinguem pela resposta.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    public async Task<ActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await _passwordReset.RequestAsync(request.Email, ClientIp.Resolve(HttpContext), UserAgent, ct);
        return Accepted(new { message = ForgotPasswordMessage });
    }

    /// <summary>Senha nova a partir do link do e-mail. Link ruim → 400.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    public async Task<ActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        await _passwordReset.ResetAsync(
            request.Token, request.NewPassword, ClientIp.Resolve(HttpContext), UserAgent, ct);
        return NoContent();
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult> Logout(CancellationToken ct)
    {
        var userId = GetUserId();
        // Sai só deste aparelho (sessão do token); os outros continuam logados
        await _authService.LogoutAsync(userId, AuthService.GetSessionId(User), ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken ct)
    {
        var userId = GetUserId();
        var user = await _authService.GetByIdAsync(userId, ct);
        if (user is null)
            return NotFound(new { message = "Usuário não encontrado." });
        return Ok(user);
    }

    private string? UserAgent => Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}

public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}
