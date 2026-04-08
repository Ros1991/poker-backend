using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Rotas diretas para torneios (sem necessidade de homeGameId na URL).
/// Usado pelo frontend quando já se tem o tournamentId.
/// </summary>
[ApiController]
[Route("api/v1/tournaments")]
[Authorize]
public class TournamentDirectController : ControllerBase
{
    private readonly TournamentService _tournamentService;

    public TournamentDirectController(TournamentService tournamentService)
    {
        _tournamentService = tournamentService;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TournamentResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _tournamentService.GetByIdAsync(id, ct);
        if (result is null)
            return NotFound(new { message = "Torneio não encontrado." });
        return Ok(result);
    }
}
