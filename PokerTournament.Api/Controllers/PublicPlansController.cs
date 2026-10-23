using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.Plans;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Catálogo de planos sem login (SPEC-20261023-1205): a ÚNICA fonte de preços e limites do frontend
/// (landing, boas-vindas, Meu plano, modal de upgrade). Vem do PlanCatalog em código; cache de 5 min.
/// </summary>
[ApiController]
[Route("api/v1/public/plans")]
[AllowAnonymous]
public class PublicPlansController : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<PlanDefinition>> GetAll()
    {
        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(PlanCatalog.All);
    }
}
