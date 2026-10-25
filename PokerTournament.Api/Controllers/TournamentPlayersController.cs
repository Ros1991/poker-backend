using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Api.Authorization;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Jogadores pelo torneio (SPEC-20261023-1213): cadastrar jogador novo e pôr/trocar a foto, no painel e no terminal.
/// O TenantAccessFilter confere o torneio da rota (organizador: dono/membro/Admin; dealer: o torneio do token).
/// Liberado ao dealer autorizado com a regra F5 na foto (só quem ainda não tem foto ou está inscrito neste torneio;
/// remover foto é só pelo painel, em DELETE /Persons/{id}/photo). Respostas sem contato (AvailablePlayerResponse).
/// Nenhuma checagem de plano (F6).
/// </summary>
[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/players")]
[Authorize]
public class TournamentPlayersController : ControllerBase
{
    private readonly TournamentPlayerService _players;

    public TournamentPlayersController(TournamentPlayerService players)
    {
        _players = players;
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost]
    public async Task<ActionResult<AvailablePlayerResponse>> Create(
        Guid tournamentId, [FromBody] CreateTournamentPlayerRequest request, CancellationToken ct)
    {
        // Dealer: WhatsApp repetido não vira 400 (seria um oráculo de contatos) e há teto de cadastros por torneio
        var result = await _players.CreateAsync(tournamentId, request, GetActorId(), IsScopedToken(), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("{personId:guid}/photo")]
    [RequestSizeLimit(ImageFiles.MaxRequestBytes)]
    public async Task<ActionResult<AvailablePlayerResponse>> UploadPhoto(
        Guid tournamentId, Guid personId, IFormFile? file, CancellationToken ct)
    {
        if (IsScopedToken())
        {
            var allowed = await _players.CanScopedTokenSetPhotoAsync(tournamentId, personId, ct);
            if (allowed is null)
                return NotFound(new { message = "Jogador não encontrado neste home game." });
            // 403 (e não 401): o client.ts trataria 401 como sessão vencida e derrubaria o terminal
            if (allowed == false)
                return new ObjectResult(new { message = TournamentPlayerService.DealerPhotoForbiddenMessage })
                    { StatusCode = StatusCodes.Status403Forbidden };
        }

        if (file is null || file.Length == 0)
            return BadRequest(new { message = ImageFiles.InvalidPhotoMessage });

        await using var stream = file.OpenReadStream();
        var result = await _players.SetPhotoAsync(tournamentId, personId, stream, file.Length, GetActorId(), ct);
        return Ok(result);
    }

    // Token de escopo restrito (dealer por QR) tem o claim "kind"; token de usuário não
    private bool IsScopedToken() =>
        !string.IsNullOrEmpty(User.FindFirst(TokenKinds.ClaimType)?.Value);

    /// <summary>Autor do histórico: id do usuário ou do pedido de dealer (claim sub).</summary>
    private Guid? GetActorId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out var id) ? id : null;
    }
}
