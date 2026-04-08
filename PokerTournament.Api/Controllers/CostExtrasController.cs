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

        // Atualizar total de custos do torneio
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament is not null)
        {
            tournament.TotalCosts += request.Amount;
            tournament.NetPrizePool = tournament.TotalPrizePool - tournament.TotalCosts;
        }

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

        var oldAmount = costExtra.Amount;
        _mapper.Map(request, costExtra);

        // Atualizar total de custos
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament is not null)
        {
            tournament.TotalCosts += (request.Amount - oldAmount);
            tournament.NetPrizePool = tournament.TotalPrizePool - tournament.TotalCosts;
        }

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

        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament is not null)
        {
            tournament.TotalCosts -= costExtra.Amount;
            tournament.NetPrizePool = tournament.TotalPrizePool - tournament.TotalCosts;
        }

        _db.CostExtras.Remove(costExtra);
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
