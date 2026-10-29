using System.Security.Claims;
using PokerTournament.Api.Authorization;
using PokerTournament.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokerTournament.Api.Hubs;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/[controller]")]
[Authorize]
public class EntriesController : ControllerBase
{
    private readonly EntryService _entryService;
    private readonly TimebankService _timebankService;
    private readonly IHubContext<TournamentHub> _hub;
    private readonly ILogger<EntriesController> _logger;

    public EntriesController(
        EntryService entryService, TimebankService timebankService, IHubContext<TournamentHub> hub, ILogger<EntriesController> logger)
    {
        _entryService = entryService;
        _timebankService = timebankService;
        _hub = hub;
        _logger = logger;
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpGet]
    public async Task<ActionResult<List<EntryResponse>>> GetAll(
        Guid tournamentId, CancellationToken ct)
    {
        var result = await _entryService.GetEntriesAsync(tournamentId, ct);
        // Dealer por QR nunca vê contato/documento dos jogadores
        if (IsScopedToken())
            foreach (var e in result)
                StripContact(e.Person);
        return Ok(result);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpGet("available-players")]
    public async Task<ActionResult<List<AvailablePlayerResponse>>> GetAvailablePlayers(
        Guid tournamentId, CancellationToken ct)
    {
        var result = await _entryService.GetAvailablePlayersAsync(tournamentId, ct);
        return Ok(result);
    }

    // SPEC-20261023-1213 (F13): o quick-add (um por vez, com regra de assento própria) saiu; painel e terminal
    // inscrevem pelo lote. Cadastrar jogador novo: POST tournaments/{id}/players (TournamentPlayersController).

    /// <summary>
    /// Inscrição individual (organizador). Nasce sempre Pendente: o dinheiro entra pela tela de pagamentos. Já senta
    /// o jogador (SPEC-20261023-1214) quando o plano do dono tem mesas — a inscrição nunca é recusada por plano.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<EntryResponse>> Register(
        Guid tournamentId, [FromBody] CreateEntryRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _entryService.RegisterEntryAsync(tournamentId, request, userId, ct);
        return CreatedAtAction(nameof(GetAll), new { tournamentId }, result);
    }

    /// <summary>
    /// Inscrição em lote — a ÚNICA rota que o painel e o terminal usam (F13), liberada ao dealer autorizado.
    /// Todas nascem Pendentes (sem "buy-in pago"). Devolve { entries, seating } (SPEC-20261023-1214, MA16): a
    /// inscrição já senta todo mundo (abre mesa e equilibra quando enche) se o plano do dono tem mesas.
    /// O ScopedTokenContactFilter tira contato das pessoas da resposta quando é o dealer.
    /// </summary>
    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("bulk")]
    public async Task<ActionResult<BulkCreateEntriesResponse>> RegisterBulk(
        Guid tournamentId, [FromBody] BulkCreateEntriesRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _entryService.RegisterEntriesBulkAsync(tournamentId, request, userId, ct);
        return Ok(result);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("{entryId:guid}/rebuy")]
    
    public async Task<ActionResult<EntryResponse>> Rebuy(
        Guid tournamentId, Guid entryId, [FromBody] RebuyRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _entryService.RegisterRebuyAsync(tournamentId, entryId, request, userId, ct);
        return Ok(result);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("{entryId:guid}/addon")]
    
    public async Task<ActionResult<EntryResponse>> Addon(
        Guid tournamentId, Guid entryId, [FromBody] AddonRequest? request, CancellationToken ct)
    {
        var userId = GetUserId();
        // SPEC-20261027-1311: { quantity } (1 a 10, até o máximo do torneio); sem corpo = 1 add-on
        var result = await _entryService.RegisterAddonAsync(tournamentId, entryId, request, userId, ct);
        return Ok(result);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("{entryId:guid}/eliminate")]
    
    public async Task<ActionResult<EliminationResultResponse>> Eliminate(
        Guid tournamentId, Guid entryId, [FromBody] EliminateRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _entryService.EliminatePlayerAsync(tournamentId, entryId, request, userId, ct);
        // O link do WhatsApp carrega o telefone do jogador: não vai para o dealer por QR
        if (IsScopedToken())
            result.WhatsAppLink = null;
        return Ok(result);
    }

    [HttpDelete("{entryId:guid}/rebuy")]
    public async Task<ActionResult<EntryResponse>> RemoveRebuy(
        Guid tournamentId, Guid entryId, CancellationToken ct)
    {
        var result = await _entryService.RemoveRebuyAsync(tournamentId, entryId, GetUserId(), ct);
        return Ok(result);
    }

    [HttpDelete("{entryId:guid}/addon")]
    public async Task<ActionResult<EntryResponse>> RemoveAddon(
        Guid tournamentId, Guid entryId, CancellationToken ct)
    {
        var result = await _entryService.RemoveAddonAsync(tournamentId, entryId, GetUserId(), ct);
        return Ok(result);
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("{entryId:guid}/undo-elimination")]
    public async Task<ActionResult<EntryResponse>> UndoElimination(
        Guid tournamentId, Guid entryId, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _entryService.UndoEliminationAsync(tournamentId, entryId, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Timebank virtual (SPEC-20261029-1722): "Usar timebank" — tira 1 do jogador e grava o uso (com os segundos) no
    /// histórico. A contagem é do celular do dealer (D4); esta rota só registra. { actionId } opcional: o "Tentar de novo"
    /// manda o mesmo id e não desconta duas vezes. 400 sem timebank no torneio, com 0 restantes ou jogador fora do jogo.
    /// </summary>
    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("{entryId:guid}/timebank/use")]
    public async Task<ActionResult<TimebankResultResponse>> UseTimebank(
        Guid tournamentId, Guid entryId, [FromBody] TimebankUseRequest? request, CancellationToken ct)
    {
        var result = await _timebankService.UseAsync(tournamentId, entryId, request, GetUserId(), ct);
        if (IsScopedToken())
            StripContact(result.Entry.Person);
        await NotifyEntryAsync(tournamentId, result.Entry);
        return Ok(result);
    }

    /// <summary>Timebank (SPEC-20261029-1722): { delta: 1 | -1 } — o dealer ou o organizador dá ou tira um; nunca abaixo de 0.</summary>
    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("{entryId:guid}/timebank/adjust")]
    public async Task<ActionResult<TimebankResultResponse>> AdjustTimebank(
        Guid tournamentId, Guid entryId, [FromBody] TimebankAdjustRequest? request, CancellationToken ct)
    {
        var result = await _timebankService.AdjustAsync(tournamentId, entryId, request, GetUserId(), ct);
        if (IsScopedToken())
            StripContact(result.Entry.Person);
        await NotifyEntryAsync(tournamentId, result.Entry);
        return Ok(result);
    }

    /// <summary>
    /// Evento SignalR "EntryUpdate" ao grupo do torneio (SPEC-20261029-1722) depois do timebank: o painel aberto recarrega as
    /// inscrições na hora (o terminal também). Só ids e o número — nada de contato. Melhor esforço: falha vai para o log.
    /// </summary>
    private async Task NotifyEntryAsync(Guid tournamentId, EntryResponse entry)
    {
        try
        {
            await _hub.Clients.Group($"tournament_{tournamentId}")
                .SendAsync("EntryUpdate", new { tournamentId, entryId = entry.Id, entry.TimebanksRemaining }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível avisar o torneio {TournamentId} do timebank", tournamentId);
        }
    }

    [HttpDelete("{entryId:guid}")]
    public async Task<ActionResult> Delete(
        Guid tournamentId, Guid entryId, CancellationToken ct)
    {
        // Quem excluiu vai na atividade "Inscrição excluída" do histórico (SPEC-20261023-1217, L5)
        await _entryService.DeleteEntryAsync(tournamentId, entryId, GetUserId(), ct);
        return NoContent();
    }

    // Token de escopo restrito (dealer por QR) tem o claim "kind"; token de usuário não
    private bool IsScopedToken() =>
        !string.IsNullOrEmpty(User.FindFirst(TokenKinds.ClaimType)?.Value);

    private static void StripContact(PersonResponse? person)
    {
        if (person is null) return;
        person.Email = null;
        person.Whatsapp = null;
        person.Document = null;
        person.Notes = null;
        // Conta vinculada (SPEC-20261029-1725): o dealer não sabe de quem é a conta
        person.LinkedUserId = null;
        person.LinkedUserName = null;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}
