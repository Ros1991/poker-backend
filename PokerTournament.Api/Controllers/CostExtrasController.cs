using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Knockout;
using PokerTournament.Application.Pix;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Api.Filters;
using PokerTournament.Application.Plans;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/cost-extras")]
[Authorize]
// SPEC-20261023-1205: recurso exclusivo do plano pago (402 feature_locked no Mesa, inclusive para o token de dealer)
[RequirePlanFeature(PlanFeature.CostExtras)]
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
        // Custos criados juntos (ex.: os padrão da estrutura, na criação do torneio) têm o mesmo CreatedAt:
        // desempate pela descrição para a ordem não variar (SPEC-20261023-1215, CP15)
        var costExtras = await _db.CostExtras
            .Where(c => c.TournamentId == tournamentId)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Description)
            .ToListAsync(ct);

        // Quanto cada custo recebeu direto dos jogadores (revisão geral 2): a aba Custos só oferece "Reverter pagamento"
        // quando há parte paga pelo caixa — o que os jogadores pagaram se desfaz pelo estorno, no histórico. Pago e status
        // pela regra única (jogadores + caixa, limitado ao valor), também para dado antigo ainda sem recálculo.
        var credits = await CostCredits.ByCostAsync(_db, tournamentId, costExtras, ct);

        return Ok(costExtras.Select(c =>
        {
            var byPlayers = credits.GetValueOrDefault(c.Id);
            var (paid, status) = CostCredits.Fitted(c, byPlayers);
            return new
            {
                c.Id,
                c.Description,
                c.Amount,
                c.CostType,
                c.Beneficiary,
                c.PixKey,
                c.PixKeyType,
                c.IsCashBox,
                PaidAmount = paid,
                PaidByPlayers = byPlayers,
                PaidByCashBox = Math.Max(0m, c.PaidByCashBox),
                OverReceived = CostCredits.OverReceived(c, byPlayers),
                PaymentStatus = status,
                c.PaymentMethod,
                c.PaidAt,
                c.Notes,
                c.CreatedAt
            };
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
        // Chave no formato do DICT (SPEC-20261023-1218): pixKeyType do pedido é a ESCOLHA da tela; o gravado é o tipo
        // da chave normalizada (o pedido nunca grava o tipo direto)
        costExtra.PixKey = PixKeyNormalizer.NormalizeForWrite(request.PixKey, request.PixKeyType);
        costExtra.PixKeyType = PixKeyNormalizer.TypeOf(costExtra.PixKey);
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

        // Editar para menos do que os jogadores já pagaram direto a este custo: 400 (revisão geral 2) — o excedente ficaria
        // sem dono. Só quando o valor desce e o pedido é o valor gravado (o ranking em % é sempre recalculado).
        var recalculated = costExtra.CostType == "RankingAccumulated" && await _db.Tournaments
            .AnyAsync(t => t.Id == tournamentId && t.RankingContribMode == "Percent" && t.RankingContribValue > 0, ct);
        if (!recalculated && request.Amount < costExtra.Amount - CostCredits.HalfCent)
        {
            var credits = await CostCredits.OfCostAsync(_db, costExtra, ct);
            if (request.Amount < credits - CostCredits.HalfCent)
                return BadRequest(new { message = CostCredits.ReduceBelowCreditsMessage(credits) });
        }

        // Preserva o CostType (request manual sempre traz "Manual"/vazio) e o IsCashBox (a tela nunca o edita;
        // o Caixa é único por torneio e tem regra própria). Beneficiário e chave PIX vêm do pedido: a aba Custos
        // manda todos os campos do custo (SPEC-20261023-1215, CP9).
        var preservedCostType = costExtra.CostType;
        var preservedIsCashBox = costExtra.IsCashBox;
        var currentPixKey = costExtra.PixKey;
        _mapper.Map(request, costExtra);
        costExtra.CostType = preservedCostType;
        costExtra.IsCashBox = preservedIsCashBox;
        // SPEC-20261023-1218: chave nova no formato do DICT (a MESMA chave antiga que não normaliza fica como está);
        // tipo = o da chave gravada
        costExtra.PixKey = PixKeyNormalizer.NormalizeForUpdate(request.PixKey, request.PixKeyType, currentPixKey);
        costExtra.PixKeyType = PixKeyNormalizer.TypeOf(costExtra.PixKey);
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

        // Com pagamento ou abatimento valendo (pago pelo caixa ou por jogador), não exclui (revisão geral 2): a FK do
        // lançamento é ON DELETE SET NULL e o PIX pago a este custo passaria a aparecer como do caixa no detalhamento
        if (costExtra.PaidAmount > 0 || costExtra.PaidByCashBox > 0
            || await _db.Transactions.AnyAsync(t => t.TournamentId == tournamentId && t.PixDestinationId == id
                                                 && t.ReversedAt == null, ct))
            return BadRequest(new { message = CostCredits.DeleteWithPaymentsMessage });

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

        // O caixa entrega o que falta (valor − o que jogadores e caixa já pagaram); a parte do caixa fica guardada à parte
        // (revisão geral 2): se o custo encolher, o excedente aparece como "recebidos a mais"; se voltar a crescer, conta de novo
        CostCredits.PayRestFromCashBox(costExtra, await CostCredits.OfCostAsync(_db, costExtra, ct));
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

        // Desfaz SÓ a parte paga pelo caixa (revisão geral 2): o que os jogadores pagaram direto ao custo continua valendo
        // (pela mesma regra do detalhamento) — senão o limite por destino voltaria a liberar o valor inteiro
        var credits = await CostCredits.OfCostAsync(_db, costExtra, ct);
        if (costExtra.PaidByCashBox < CostCredits.HalfCent)
        {
            if (credits > CostCredits.HalfCent)
                return BadRequest(new { message = CostCredits.UnpayOnlyPlayersMessage });
            // Nada pago (nem pelo caixa, nem por jogador): nada a reverter (pago e status pela regra única)
            CostCredits.FitPaid(costExtra, credits);
            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "Pagamento revertido." });
        }

        CostCredits.UndoCashBox(costExtra, credits);
        if (costExtra.PaymentStatus != nameof(Domain.Enums.PaymentStatus.Paid))
            costExtra.PaymentMethod = null;

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
                // Bounty não é prize pool (SPEC-20261026-1909): fica fora da base do ranking %
                var baseForPct = tournament.TotalPrizePool - staff - manual - tournament.TotalBounties;
                var raw = (tournament.RankingContribValue!.Value / 100m) * baseForPct;
                rankingCost.Amount = Math.Max(0m, Math.Round(raw / 10m, MidpointRounding.AwayFromZero) * 10m);
            }
        }

        // Mesma regra do EntryService/TournamentService: o pago nunca passa do valor e a parte do caixa não se perde (revisão geral 2)
        await CostCredits.FitAllAsync(_db, tournamentId, costs, ct);

        tournament.TotalCosts = costs.Sum(c => c.Amount);
        KnockoutRules.ApplyNet(tournament);
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
