using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PokerTournament.Api.Authorization;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Recursos de IA vistos pelo app (SPEC-20261027-1312): só a leitura dos recursos ligados e o aviso de uso feito no
/// aparelho — nada de configuração (essa fica em <c>api/v1/admin/ai</c>, só Admin). Duas formas de cada rota:
/// <list type="bullet">
/// <item><c>api/v1/ai/…</c>: qualquer conta logada (painel, cadastro rápido, Meu perfil);</item>
/// <item><c>api/v1/tournaments/{tournamentId}/ai/…</c>: o terminal do dealer — o token do QR só vale nas rotas do
/// próprio torneio (e é só nelas que o app o manda); conta logada também passa, pelo isolamento de sempre.</item>
/// </list>
/// </summary>
[ApiController]
[Authorize]
public class AiController : ControllerBase
{
    private readonly AiFeatureService _features;

    public AiController(AiFeatureService features)
    {
        _features = features;
    }

    /// <summary>Chaves dos recursos de IA ligados agora (ex.: <c>["tirar-o-fundo"]</c>); IA desligada → lista vazia.</summary>
    [HttpGet("api/v1/ai/features")]
    [HttpGet("api/v1/tournaments/{tournamentId:guid}/ai/features")]
    [AllowTokenKind(TokenKinds.Dealer)]
    public async Task<ActionResult<AiAvailableFeaturesResponse>> Features(CancellationToken ct) =>
        Ok(await _features.GetAvailableAsync(ct));

    /// <summary>
    /// Uso de um recurso que roda no aparelho ("tirar-o-fundo"), avisado pelo app a cada foto salva com o fundo tirado.
    /// Recurso ou IA desligados → 400. Limite de 30 avisos por minuto por conta/dealer.
    /// </summary>
    [HttpPost("api/v1/ai/usage")]
    [HttpPost("api/v1/tournaments/{tournamentId:guid}/ai/usage")]
    [AllowTokenKind(TokenKinds.Dealer)]
    [EnableRateLimiting("ai-usage")]
    public async Task<IActionResult> Usage(Guid? tournamentId, [FromBody] RecordAiUsageRequest request, CancellationToken ct)
    {
        var sub = (User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub"))?.Value;
        var parsed = Guid.TryParse(sub, out var id) ? id : (Guid?)null;
        AiDeviceCaller caller;
        if (User.FindFirst(TokenKinds.ClaimType)?.Value == TokenKinds.Dealer)
        {
            // A política já conferiu que o torneio da rota é o do token
            var tokenTournament = Guid.TryParse(User.FindFirst(TokenKinds.TournamentClaim)?.Value, out var tid) ? tid : (Guid?)null;
            caller = new AiDeviceCaller(null, false, parsed, tokenTournament);
        }
        else
        {
            caller = new AiDeviceCaller(parsed, User.IsInRole("Admin"), null, null);
            // Rota do torneio: o TenantAccessFilter já conferiu o acesso; o home game vem do torneio
            if (tournamentId is { } routeTournament && request.TournamentId is null)
                request.TournamentId = routeTournament;
        }
        await _features.RecordDeviceUsageAsync(request, caller, ct);
        return NoContent();
    }
}
