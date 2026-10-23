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
    /// Entrar. E-mail ou senha errados → 400 neutro. Senha certa com o e-mail ainda não
    /// confirmado → 403 <c>{ code: "email_not_confirmed", message }</c> (sem sessão).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, UserAgent, ct);
        return Ok(result);
    }

    /// <summary>
    /// Cadastro público: 202 com <c>{ email, message }</c> e SEM sessão — a conta só entra depois
    /// de confirmar o e-mail pelo link (48 h). E-mail de conta confirmada → 400 "E-mail já está em
    /// uso."; e-mail de conta ainda não confirmada → sobrescreve os dados e reenvia (202 igual).
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterAsync(request, ClientIp.Resolve(HttpContext), UserAgent, ct);
        return Accepted(result);
    }

    /// <summary>
    /// Confirma o e-mail pelo token do link {PUBLIC_APP_URL}/confirmar-email/{token}: 204 (não loga).
    /// Link inválido, expirado, já usado ou substituído por um mais novo → 400.
    /// </summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anon")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request, CancellationToken ct)
    {
        await _emailConfirmation.ConfirmAsync(request.Token, ClientIp.Resolve(HttpContext), UserAgent, ct);
        return NoContent();
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
