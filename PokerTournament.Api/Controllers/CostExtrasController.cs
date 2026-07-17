using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/cost-extras")]
[Authorize]
public class CostExtrasController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;

    public CostExtrasController(IAppDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(Guid tournamentId, CancellationToken ct)
    {
        var costExtras = await _db.CostExtras
            .Where(c => c.TournamentId == tournamentId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return Ok(costExtras.Select(c => new
        {
            c.Id,
            c.Description,
            c.Amount,
            c.CostType,
            c.Beneficiary,
            c.PixKey,
            c.PixKeyType,
            c.IsCashBox,
            c.PaidAmount,
            c.PaymentStatus,
            c.PaymentMethod,
            c.PaidAt,
            c.Notes,
            c.CreatedAt
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> GetById(Guid tournamentId, Guid id, CancellationToken ct)
    {
        var costExtra = await _db.CostExtras
            .FirstOrDefaultAsync(c => c.Id == id && c.TournamentId == tournamentId, ct);

        if (costExtra is null)
            return NotFound(new { message = "Custo extra não encontrado." });

        return Ok(costExtra);
    }

    [HttpPost]
    
    public async Task<ActionResult> Create(
        Guid tournamentId, [FromBody] CreateCostExtraRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var costExtra = _mapper.Map<CostExtra>(request);
        costExtra.TournamentId = tournamentId;
        costExtra.PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending);
        costExtra.CreatedBy = userId;

        _db.CostExtras.Add(costExtra);
        await _db.SaveChangesAsync(ct);

        // Recalcular custos/líquido (custo manual reduz a base do ranking %).
        await RecomputeTournamentFinancials(tournamentId, ct);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { tournamentId, id = costExtra.Id },
            new { costExtra.Id, costExtra.Description, costExtra.Amount });
    }

    [HttpPut("{id:guid}")]
    
    public async Task<ActionResult> Update(
        Guid tournamentId, Guid id, [FromBody] CreateCostExtraRequest request, CancellationToken ct)
    {
        var costExtra = await _db.CostExtras
            .FirstOrDefaultAsync(c => c.Id == id && c.TournamentId == tournamentId, ct);

        if (costExtra is null)
            return NotFound(new { message = "Custo extra não encontrado." });

        // Preserva o CostType (request manual sempre traz "Manual"/vazio).
        var preservedCostType = costExtra.CostType;
        _mapper.Map(request, costExtra);
        costExtra.CostType = preservedCostType;
        await _db.SaveChangesAsync(ct);

        await RecomputeTournamentFinancials(tournamentId, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new { costExtra.Id, costExtra.Description, costExtra.Amount });
    }

    [HttpDelete("{id:guid}")]
    
    public async Task<ActionResult> Delete(Guid tournamentId, Guid id, CancellationToken ct)
    {
        var costExtra = await _db.CostExtras
            .FirstOrDefaultAsync(c => c.Id == id && c.TournamentId == tournamentId, ct);

        if (costExtra is null)
            return NotFound(new { message = "Custo extra não encontrado." });

        // Staff automático é editável mas não removível. O acumulado do ranking PODE
        // ser removido (o valor volta ao prêmio líquido — organizador decide não
        // acumular neste torneio); os null-checks de recálculo/accrual não o recriam.
        if (costExtra.CostType == "Staff")
            return BadRequest(new { message = "Custo automático de staff não pode ser removido. Edite o valor se necessário." });

        if (costExtra.CostType == "RankingAccumulated" && costExtra.PaidAmount > 0)
            return BadRequest(new { message = "Este custo já tem pagamento/abatimento registrado. Estorne antes de excluir." });

        _db.CostExtras.Remove(costExtra);
        await _db.SaveChangesAsync(ct);

        await RecomputeTournamentFinancials(tournamentId, ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/pay")]
    public async Task<ActionResult> Pay(
        Guid tournamentId, Guid id, [FromBody] PayCostRequest? request, CancellationToken ct)
    {
        var costExtra = await _db.CostExtras
            .FirstOrDefaultAsync(c => c.Id == id && c.TournamentId == tournamentId, ct);

        if (costExtra is null)
            return NotFound(new { message = "Custo extra não encontrado." });

        costExtra.PaidAmount = costExtra.Amount;
        costExtra.PaymentStatus = nameof(Domain.Enums.PaymentStatus.Paid);
        costExtra.PaymentMethod = request?.Method ?? "Dinheiro";
        costExtra.PaidAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Custo extra pago com sucesso." });
    }

    [HttpPost("{id:guid}/unpay")]
    public async Task<ActionResult> Unpay(Guid tournamentId, Guid id, CancellationToken ct)
    {
        var costExtra = await _db.CostExtras
            .FirstOrDefaultAsync(c => c.Id == id && c.TournamentId == tournamentId, ct);

        if (costExtra is null)
            return NotFound(new { message = "Custo extra não encontrado." });

        costExtra.PaidAmount = 0;
        costExtra.PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending);
        costExtra.PaymentMethod = null;
        costExtra.PaidAt = null;

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Pagamento revertido." });
    }

    /// <summary>
    /// Recalcula custos automáticos e líquido. Ranking % = múltiplo de 10 de
    /// (% × (receita − staff − custos manuais)). Chamar após gravar mudanças em custos.
    /// </summary>
    private async Task RecomputeTournamentFinancials(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);
        if (tournament is null) return;

        var costs = await _db.CostExtras
            .Where(c => c.TournamentId == tournamentId)
            .ToListAsync(ct);

        if (tournament.RankingContribMode == "Percent" && tournament.RankingContribValue is > 0)
        {
            var rankingCost = costs.FirstOrDefault(c => c.CostType == "RankingAccumulated");
            if (rankingCost is not null)
            {
                var staff = costs.Where(c => c.CostType == "Staff").Sum(c => c.Amount);
                var manual = costs.Where(c => c.CostType != "Staff" && c.CostType != "RankingAccumulated").Sum(c => c.Amount);
                var baseForPct = tournament.TotalPrizePool - staff - manual;
                var raw = (tournament.RankingContribValue!.Value / 100m) * baseForPct;
                rankingCost.Amount = Math.Max(0m, Math.Round(raw / 10m, MidpointRounding.AwayFromZero) * 10m);
            }
        }

        tournament.TotalCosts = costs.Sum(c => c.Amount);
        tournament.NetPrizePool = tournament.TotalPrizePool - tournament.TotalCosts;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }
}

public class PayCostRequest
{
    public string? Method { get; set; }
}
