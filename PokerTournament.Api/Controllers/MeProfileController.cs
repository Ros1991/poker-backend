using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Api.Http;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Perfil público e privacidade da PRÓPRIA conta (SPEC-20261108-1636), ao lado do <see cref="MeController"/>: o
/// <c>GET me</c> já devolve os campos (handle, cidade/UF, bio e os quatro controles); aqui ficam as gravações e o
/// "jogou com você". Sem [AllowTokenKind]: o token de dealer (QR) não chega aqui (403).
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MeProfileController : ControllerBase
{
    private const string NotFoundMessage = "Usuário não encontrado.";

    private readonly PlayerProfileService _profiles;
    private readonly PlayedWithService _playedWith;

    public MeProfileController(PlayerProfileService profiles, PlayedWithService playedWith)
    {
        _profiles = profiles;
        _playedWith = playedWith;
    }

    /// <summary>
    /// Salva o perfil público: <c>{ handle, city?, state?, bio? }</c>. Handle fora do formato, reservado ou já em uso
    /// → 400 com a mensagem; UF fora da lista → 400; bio acima de 200 → 400. Devolve o perfil da conta (como GET me).
    /// </summary>
    [HttpPut("public-profile")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfileResponse>> UpdatePublicProfile(
        [FromBody] UpdatePublicProfileRequest request, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _profiles.UpdatePublicProfileAsync(userId, request, Ip, UserAgent, ct));
    }

    /// <summary>
    /// Salva os controles de privacidade: <c>{ profileVisibility?, friendRequestsFrom?, messagesFrom?, showInPlayedWith? }</c>
    /// (ausente = não mexe). Valor fora das opções → 400. Devolve o perfil da conta (como GET me).
    /// </summary>
    [HttpPut("privacy")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfileResponse>> UpdatePrivacy(
        [FromBody] UpdatePrivacyRequest request, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _profiles.UpdatePrivacyAsync(userId, request, Ip, UserAgent, ct));
    }

    /// <summary>
    /// "Jogou com você": contas que jogaram os mesmos torneios (dos dois lados com a opção ligada; a própria desligada =
    /// lista vazia), com quantas vezes e a última data, por vezes e data. <c>?limit=</c> padrão 8, máx. 50.
    /// </summary>
    [HttpGet("played-with")]
    [ProducesResponseType(typeof(List<PlayedWithResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PlayedWithResponse>>> GetPlayedWith(
        [FromQuery] int limit = PlayedWithService.DefaultLimit, CancellationToken ct = default)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _playedWith.ListAsync(userId, limit, ct));
    }

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
}
