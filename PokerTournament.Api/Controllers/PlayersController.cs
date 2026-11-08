using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Perfil público do jogador (SPEC-20261108-1636): qualquer conta logada (qualquer papel) abre
/// <c>/jogador/{handle}</c>. Sem escopo de home game e fora do filtro de tenant (a rota não tem homeGameId nem
/// tournamentId). Sem [AllowTokenKind]: o token de dealer (QR) não chega aqui (403). A regra de visibilidade e o que
/// pode sair estão no <see cref="PlayerProfileService"/>.
/// </summary>
[ApiController]
[Route("api/v1/players")]
[Authorize]
public class PlayersController : ControllerBase
{
    private const string NotFoundMessage = "Usuário não encontrado.";

    private readonly PlayerProfileService _profiles;

    public PlayersController(PlayerProfileService profiles)
    {
        _profiles = profiles;
    }

    /// <summary>
    /// O perfil público de <c>{handle}</c> para quem está logado: foto, apelido, handle, cidade/UF, bio, clubes em comum,
    /// números da temporada; com a visibilidade fechada para quem vê, só foto e apelido (<c>isPrivate</c>).
    /// <c>?preview=stranger</c> aplica a regra como para um desconhecido (pré-visualização do Meu perfil). 404 para
    /// handle inexistente, conta inativa/não confirmada ou bloqueio.
    /// </summary>
    [HttpGet("{handle}")]
    [ProducesResponseType(typeof(PlayerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlayerProfileResponse>> GetByHandle(
        string handle, [FromQuery] string? preview, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var asStranger = string.Equals(preview, PlayerProfileService.StrangerPreview, StringComparison.OrdinalIgnoreCase);
        var profile = await _profiles.GetByHandleAsync(handle, userId, asStranger, ct);
        return profile is null
            ? NotFound(new { message = PlayerProfileService.ProfileNotFoundMessage })
            : Ok(profile);
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
