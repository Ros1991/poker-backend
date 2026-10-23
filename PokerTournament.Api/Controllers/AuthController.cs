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

    private readonly AuthService _authService;
    private readonly PasswordResetService _passwordReset;

    public AuthController(AuthService authService, PasswordResetService passwordReset)
    {
        _authService = authService;
        _passwordReset = passwordReset;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, UserAgent, ct);
        return Ok(result);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterAsync(request, UserAgent, ct);
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
