using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Geo;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// Consulta de CEP (SPEC-20261108-1634, decisão 1): o navegador pergunta aqui e o servidor consulta o ViaCEP (cache em
/// memória por CEP, 5 s de espera). Qualquer conta logada; o token de dealer não chega (TokenKindHandler). Sem
/// {homeGameId} na rota, o TenantAccessFilter não age — não há dado de ninguém aqui.
/// </summary>
[ApiController]
[Route("api/v1/cep")]
[Authorize]
public class CepController : ControllerBase
{
    private readonly ViaCepClient _viaCep;

    public CepController(ViaCepClient viaCep)
    {
        _viaCep = viaCep;
    }

    /// <summary>
    /// 200 com rua/bairro/cidade/UF; 400 CEP fora de formato (não são 8 dígitos); 404 CEP não encontrado (a tela libera
    /// preencher à mão); 503 ViaCEP fora do ar (idem).
    /// </summary>
    [HttpGet("{cep}")]
    [ProducesResponseType(typeof(CepResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CepResponse>> Get(string cep, CancellationToken ct)
    {
        var digits = ViaCepClient.NormalizeCep(cep);
        if (digits is null)
            return BadRequest(new { message = ViaCepClient.InvalidMessage });

        var result = await _viaCep.LookupAsync(digits, ct);
        return result.Status switch
        {
            CepLookupStatus.Found => Ok(result.Address),
            CepLookupStatus.NotFound => NotFound(new { message = ViaCepClient.NotFoundMessage }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ViaCepClient.UnavailableMessage }),
        };
    }
}
