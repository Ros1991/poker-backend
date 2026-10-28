using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Api.Http;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Administração → "Inteligência artificial" (SPEC-20261027-1312): só o Admin do sistema (policy "Admin"; os demais
/// papéis e o token de dealer recebem 403). Fino: as regras ficam no <see cref="AiAdminService"/>. A chave da API entra
/// por <c>PUT key</c> e nunca sai: as respostas trazem só a dica "sk-ant-…ABCD". Toda ação grava AuditLog.
/// </summary>
[ApiController]
[Route("api/v1/admin/ai")]
[Authorize(Policy = "Admin")]
public class AdminAiController : ControllerBase
{
    private readonly AiAdminService _admin;

    public AdminAiController(AiAdminService admin)
    {
        _admin = admin;
    }

    /// <summary>
    /// Configurações (liga/desliga, modelo padrão, limite), situação da chave (dica, último teste), gasto do mês contra o
    /// limite (aviso a partir de 80 %), recursos com liga/desliga e usos do mês, preços por modelo, uso do mês por
    /// recurso e por modelo e as últimas chamadas.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AiOverviewResponse>> Get(CancellationToken ct) =>
        Ok(await _admin.GetOverviewAsync(ct));

    /// <summary>Liga/desliga geral, modelo padrão (um dos modelos da tabela de preços) e limite mensal em US$ (0 = nenhum uso pago).</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<AiOverviewResponse>> UpdateSettings([FromBody] UpdateAiSettingsRequest request, CancellationToken ct) =>
        Ok(await _admin.UpdateSettingsAsync(request, Actor, ct));

    /// <summary>Cadastrar ou trocar a chave (cifrada com AES-256-GCM). Sem AI_SECRETS_KEY no servidor → 409.</summary>
    [HttpPut("key")]
    public async Task<ActionResult<AiOverviewResponse>> SetKey([FromBody] SetAiKeyRequest request, CancellationToken ct) =>
        Ok(await _admin.SetKeyAsync(request, Actor, ct));

    /// <summary>Remover a chave.</summary>
    [HttpDelete("key")]
    public async Task<ActionResult<AiOverviewResponse>> RemoveKey(CancellationToken ct) =>
        Ok(await _admin.RemoveKeyAsync(Actor, ct));

    /// <summary>"Testar chave": valida a chave guardada e o modelo pela API de modelos (sem custo de tokens).</summary>
    [HttpPost("key/test")]
    public async Task<ActionResult<AiKeyTestResponse>> TestKey([FromBody] TestAiKeyRequest? request, CancellationToken ct) =>
        Ok(await _admin.TestKeyAsync(request ?? new TestAiKeyRequest(), Actor, ct));

    /// <summary>Liga/desliga um recurso de IA (chave do catálogo, ex.: "tirar-o-fundo").</summary>
    [HttpPut("features/{key}")]
    public async Task<ActionResult<AiOverviewResponse>> SetFeature(string key, [FromBody] SetAiFeatureRequest request, CancellationToken ct) =>
        Ok(await _admin.SetFeatureAsync(key, request.Enabled, Actor, ct));

    /// <summary>Preços de um modelo em US$ por milhão de tokens (entrada, saída, escrita e leitura de cache).</summary>
    [HttpPut("prices/{model}")]
    public async Task<ActionResult<AiOverviewResponse>> UpdatePrice(string model, [FromBody] UpdateAiPriceRequest request, CancellationToken ct) =>
        Ok(await _admin.UpdatePriceAsync(model, request, Actor, ct));

    private AdminActor Actor => new(
        Guid.TryParse((User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub"))?.Value, out var id) ? id : Guid.Empty,
        ClientIp.Resolve(HttpContext),
        Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null);
}
