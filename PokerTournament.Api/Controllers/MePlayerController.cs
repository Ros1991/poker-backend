using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Área do jogador (SPEC-20261029-1726): o que a conta logada jogou e está jogando, em qualquer home game. Sem escopo de
/// home game e fora do filtro de tenant — a autorização é só o vínculo <c>Person.UserId == conta</c> (SPEC-20261029-1725),
/// aplicado no <see cref="MePlayerService"/>. Sem [AllowTokenKind]: o token de dealer (QR) não chega aqui (403).
/// <para>
/// O id do torneio na rota se chama <c>{id}</c> de propósito: com <c>{tournamentId}</c> o <c>TenantAccessFilter</c>
/// exigiria ser dono/membro do home game e o jogador que só joga levaria 403.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MePlayerController : ControllerBase
{
    private const string NotFoundMessage = "Usuário não encontrado.";
    private const string TournamentNotFoundMessage = "Você não tem inscrição neste torneio.";
    private const string RankingNotFoundMessage = "Você não participa deste ranking.";

    private readonly MePlayerService _player;

    public MePlayerController(MePlayerService player)
    {
        _player = player;
    }

    /// <summary>
    /// Resumo de quem joga: torneios, vitórias, ITM, saldo em aberto (dos organizadores com controle de pagamentos), onde
    /// joga (filtro), os 5 mais recentes e os iniciados em que está inscrito. Sem inscrição vinculada: zeros e listas vazias
    /// (a tela mostra a dica de pedir ao organizador para cadastrar o e-mail da conta).
    /// </summary>
    [HttpGet("player")]
    [ProducesResponseType(typeof(MePlayerSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MePlayerSummaryResponse>> GetSummary(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var summary = await _player.GetSummaryAsync(userId, ct);
        return summary is null ? NotFound(new { message = NotFoundMessage }) : Ok(summary);
    }

    /// <summary>
    /// Os torneios da conta (o mais novo primeiro), paginados: <c>?homeGameId=&amp;page=&amp;pageSize=</c> (padrão 20, máx. 50).
    /// Por linha: torneio, data, onde (nome do clube ou "Torneios de &lt;organizador&gt;"), status, posição, rebuys, add-ons,
    /// devido/pago/saldo, prêmio e bounties — iguais aos da aba Pagamentos/Jogadores do organizador.
    /// </summary>
    [HttpGet("tournaments")]
    [ProducesResponseType(typeof(PaginatedResponse<MyTournamentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginatedResponse<MyTournamentResponse>>> GetTournaments(
        [FromQuery] Guid? homeGameId, [FromQuery] int page = 1, [FromQuery] int pageSize = MePlayerService.DefaultPageSize,
        CancellationToken ct = default)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _player.GetTournamentsAsync(userId, homeGameId, page, pageSize, ct));
    }

    /// <summary>
    /// O torneio ao vivo para quem joga: relógio, níveis, jogando X de Y, premiação total (com Prizes), PIX do torneio
    /// (com Payments) e a ficha da conta. 404 quando a conta não tem inscrição nele — também para torneio de outro home
    /// game que existe (não diz se existe). SPEC-20261108-1633: também timebanks e "você está no tempo", a mesa dela com
    /// os lugares (D3), a bolha (com Prizes), o WhatsApp de quem responde pelo torneio, as eliminações que fez e, no
    /// torneio de ranking terminado, os pontos e a posição depois dele.
    /// </summary>
    [HttpGet("tournaments/{id:guid}")]
    [ProducesResponseType(typeof(MyTournamentLiveResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MyTournamentLiveResponse>> GetTournament(Guid id, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var live = await _player.GetLiveAsync(userId, id, ct);
        return live is null ? NotFound(new { message = TournamentNotFoundMessage }) : Ok(live);
    }

    /// <summary>
    /// Os lançamentos das inscrições da conta no torneio (SPEC-20261108-1633): inscrição, rebuys, add-ons, pagamentos,
    /// prêmio pago, eliminação (dela e as que fez), mesa/lugar e timebank, com a hora de cada um. Nada dos outros. 404 sem
    /// inscrição da conta.
    /// </summary>
    [HttpGet("tournaments/{id:guid}/history")]
    [ProducesResponseType(typeof(List<MyHistoryItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<MyHistoryItemResponse>>> GetTournamentHistory(Guid id, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var items = await _player.GetHistoryAsync(userId, id, ct);
        return items is null ? NotFound(new { message = TournamentNotFoundMessage }) : Ok(items);
    }

    /// <summary>
    /// Meus rankings (SPEC-20261108-1633): cada ranking em que a conta tem inscrição vinculada em algum torneio, com a
    /// classificação completa (o mesmo DTO da página pública /r/{slug}: apelido, foto, pontos, etapas) e a posição/pontos
    /// da conta. Conta sem inscrição: lista vazia.
    /// </summary>
    [HttpGet("rankings")]
    [ProducesResponseType(typeof(List<MyRankingResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<MyRankingResponse>>> GetRankings(CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        return Ok(await _player.GetRankingsAsync(userId, ct));
    }

    /// <summary>Um ranking da conta. 404 quando ela não tem inscrição em nenhum torneio dele (não diz se o ranking existe).</summary>
    [HttpGet("rankings/{id:guid}")]
    [ProducesResponseType(typeof(MyRankingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MyRankingResponse>> GetRanking(Guid id, CancellationToken ct)
    {
        if (UserId is not { } userId)
            return NotFound(new { message = NotFoundMessage });
        var ranking = await _player.GetRankingAsync(userId, id, ct);
        return ranking is null ? NotFound(new { message = RankingNotFoundMessage }) : Ok(ranking);
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
