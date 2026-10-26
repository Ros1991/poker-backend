using Microsoft.AspNetCore.Mvc.Filters;
using PokerTournament.Application.Plans;
using PokerTournament.Application.Services;

namespace PokerTournament.Api.Filters;

/// <summary>
/// Recurso exclusivo de plano pago (SPEC-20261023-1205) na classe ou na ação. O
/// <see cref="PlanFeatureFilter"/> confere o plano efetivo do DONO do home game/torneio da rota; sem o
/// recurso → HTTP 402 feature_locked. Atributo da ação vale sobre o da classe.
/// Só em criação de recurso e recursos exclusivos — NUNCA em ação de torneio já criado (inscrição,
/// rebuy, add-on, eliminação, desfazer, timer, status, edição, exclusão, histórico).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePlanFeatureAttribute : Attribute
{
    public RequirePlanFeatureAttribute(PlanFeature feature)
    {
        Feature = feature;
    }

    public PlanFeature Feature { get; }
}

/// <summary>
/// Filtro global, registrado DEPOIS do <see cref="TenantAccessFilter"/> (primeiro "é seu?", depois
/// "seu plano permite?"). Resolve o {tournamentId}/{homeGameId} da rota como o TenantAccessFilter —
/// inclusive torneio/home game excluído (IgnoreQueryFilters no PlanService: ele continua tendo dono);
/// id que nunca existiu passa e o controller responde 404.
/// Ninguém pula (SPEC-20261026-1126): nem o Admin do sistema (segue o plano do dono do home game como qualquer
/// conta) nem o token de dealer (QR) — é assim que pagamentos/mesas pelo terminal caem no 402 num torneio cujo dono
/// está no Mesa. Desligado só com Plans:Enforce = false.
/// </summary>
public class PlanFeatureFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var required = context.ActionDescriptor.EndpointMetadata
            .OfType<RequirePlanFeatureAttribute>()
            .LastOrDefault();
        if (required is null)
        {
            await next();
            return;
        }

        var plans = context.HttpContext.RequestServices.GetRequiredService<PlanService>();
        var user = context.HttpContext.User;
        // Anônimo: deixa o pipeline de autenticação decidir (os endpoints com o atributo são [Authorize])
        if (!plans.Enforce || user.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        var route = context.RouteData.Values;
        var ct = context.HttpContext.RequestAborted;
        if (TryGetGuid(route, "tournamentId", out var tournamentId))
            await plans.EnsureFeatureForTournamentAsync(tournamentId, required.Feature, ct: ct);
        else if (TryGetGuid(route, "homeGameId", out var homeGameId))
            await plans.EnsureFeatureForHomeGameAsync(homeGameId, required.Feature, ct: ct);

        await next();
    }

    private static bool TryGetGuid(Microsoft.AspNetCore.Routing.RouteValueDictionary route, string key, out Guid value)
    {
        value = Guid.Empty;
        return route.TryGetValue(key, out var raw) && Guid.TryParse(raw?.ToString(), out value);
    }
}
