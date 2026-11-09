using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PokerTournament.Api.Authorization;
using PokerTournament.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
using PokerTournament.Api.Filters;
using PokerTournament.Application.Plans;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Rotas diretas para torneios (sem necessidade de homeGameId na URL).
/// Usado pelo frontend quando já se tem o tournamentId.
/// O parâmetro de rota precisa se chamar {tournamentId}: é por ele que o
/// TenantAccessFilter exige dono/membro (com {id} o filtro não agia).
/// </summary>
[ApiController]
[Route("api/v1/tournaments")]
[Authorize]
public class TournamentDirectController : ControllerBase
{
    public const string PublicityForbiddenMessage = "Só quem gerencia o home game (dono ou administrador) muda a divulgação do torneio.";

    private readonly TournamentService _tournamentService;
    private readonly IAppDbContext _db;

    public TournamentDirectController(TournamentService tournamentService, IAppDbContext db)
    {
        _tournamentService = tournamentService;
        _db = db;
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpGet("{tournamentId:guid}")]
    public async Task<ActionResult<TournamentResponse>> GetById(Guid tournamentId, CancellationToken ct)
    {
        var result = await _tournamentService.GetByIdAsync(tournamentId, ct);
        if (result is null)
            return NotFound(new { message = "Torneio não encontrado." });
        return Ok(result);
    }

    /// <summary>
    /// Níveis de blinds do torneio sem o homeGameId na URL (SPEC-20261029-1721): o telão no celular
    /// (/dealer/{id}/telao) calcula o "Próximo intervalo" com eles, também com o token do dealer por QR
    /// (a rota com homeGameId é negada a esse token). Só leitura: editar continua na rota do home game.
    /// </summary>
    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpGet("{tournamentId:guid}/blind-levels")]
    public async Task<ActionResult<List<TournamentBlindLevelResponse>>> GetBlindLevels(Guid tournamentId, CancellationToken ct)
    {
        var result = await _tournamentService.GetBlindLevelsAsync(tournamentId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Divulgação do torneio (SPEC-20261108-1639): o atalho "Divulgar" do painel — público, descrição para quem é de fora,
    /// vagas e a porta do WhatsApp do organizador. Campo ausente = manter. Só quem GERENCIA o home game (dono, membro
    /// administrador ou Admin do sistema; o filtro de tenant deixa passar qualquer membro, por isso a conferência extra —
    /// revisão da 1639: membro comum não torna o torneio público nem liga o WhatsApp). Sem [AllowTokenKind]: o dealer por QR
    /// não divulga (403).
    /// </summary>
    [HttpPut("{tournamentId:guid}/publicity")]
    public async Task<ActionResult<TournamentResponse>> UpdatePublicity(
        Guid tournamentId, [FromBody] UpdateTournamentPublicityRequest request, CancellationToken ct)
    {
        var homeGameId = await _db.Tournaments.Where(t => t.Id == tournamentId).Select(t => (Guid?)t.HomeGameId).FirstOrDefaultAsync(ct);
        if (homeGameId is null)
            return NotFound(new { message = "Torneio não encontrado." });
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var isAdmin = User.FindFirst(ClaimTypes.Role)?.Value == "Admin";
        if (!Guid.TryParse(claim?.Value, out var userId)
            || !await HomeGameAccess.CanManageAsync(_db, homeGameId.Value, userId, isAdmin, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = PublicityForbiddenMessage });
        var result = await _tournamentService.UpdatePublicityAsync(tournamentId, request, ct);
        return Ok(result);
    }

    [HttpPut("{tournamentId:guid}/punctuality-bonus")]
    public async Task<ActionResult<TournamentResponse>> UpdatePunctualityBonus(
        Guid tournamentId, [FromBody] UpdatePunctualityBonusRequest request, CancellationToken ct)
    {
        var result = await _tournamentService.UpdatePunctualityBonusAsync(
            tournamentId, request.Count, request.Chips, ct);
        return Ok(result);
    }

    // PIX do dia é financeiro (D3): 402 feature_locked no Mesa. A chave é gravada no formato do DICT
    // (SPEC-20261023-1218): fora de formato → 400 com a mensagem; pixKeyType desempata CPF × celular.
    [RequirePlanFeature(PlanFeature.Payments)]
    [HttpPut("{tournamentId:guid}/pix-key")]
    public async Task<ActionResult<TournamentResponse>> UpdatePixKey(
        Guid tournamentId, [FromBody] UpdatePixKeyRequest request, CancellationToken ct)
    {
        var result = await _tournamentService.UpdatePixKeyAsync(tournamentId, request.PixKey, request.PixKeyType, ct);
        return Ok(result);
    }
}
