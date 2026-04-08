using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

public class PrizeService
{
    private readonly IAppDbContext _db;

    private static readonly Dictionary<int, decimal[]> DefaultPercentages = new()
    {
        [2] = [0.65m, 0.35m],
        [3] = [0.50m, 0.30m, 0.20m],
        [4] = [0.40m, 0.25m, 0.20m, 0.15m],
        [5] = [0.35m, 0.22m, 0.17m, 0.14m, 0.12m],
        [6] = [0.33m, 0.20m, 0.15m, 0.13m, 0.10m, 0.09m],
        [7] = [0.30m, 0.19m, 0.14m, 0.12m, 0.10m, 0.08m, 0.07m],
        [8] = [0.28m, 0.18m, 0.13m, 0.11m, 0.09m, 0.08m, 0.07m, 0.06m],
    };

    public PrizeService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PrizeCalculationResponse> CalculatePrizesAsync(
        Guid tournamentId, int? prizeCount = null, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var totalCosts = await _db.CostExtras
            .Where(c => c.TournamentId == tournamentId)
            .SumAsync(c => c.Amount, ct);

        var grossPrizePool = tournament.TotalPrizePool;
        var netPrizePool = grossPrizePool - totalCosts;

        if (netPrizePool <= 0)
            throw new DomainException("Prize pool líquido deve ser positivo.");

        var suggestedCount = prizeCount ?? SuggestPrizeCount(tournament.TotalEntries);

        if (suggestedCount < 2)
            throw new DomainException("Mínimo de 2 premiados.");

        var percentages = GetPercentages(suggestedCount);

        // Calcular valores brutos e arredondar para múltiplo de R$ 50
        var prizes = new List<PrizeAllocation>();
        for (int i = 0; i < suggestedCount; i++)
        {
            var rawAmount = netPrizePool * percentages[i];
            var roundedAmount = Math.Floor(rawAmount / 50m) * 50m;
            prizes.Add(new PrizeAllocation
            {
                Position = i + 1,
                Amount = roundedAmount,
                Percentage = percentages[i] * 100
            });
        }

        // Restante vai para o 1o colocado
        var totalRounded = prizes.Sum(p => p.Amount);
        var remainder = netPrizePool - totalRounded;
        prizes[0].Amount += remainder;

        // Recalcular percentuais finais
        foreach (var prize in prizes)
        {
            prize.Percentage = Math.Round((prize.Amount / netPrizePool) * 100, 1);
        }

        return new PrizeCalculationResponse
        {
            GrossPrizePool = grossPrizePool,
            TotalCosts = totalCosts,
            NetPrizePool = netPrizePool,
            SuggestedPrizeCount = suggestedCount,
            Prizes = prizes,
            Remainder = remainder
        };
    }

    public async Task<List<PrizeAllocation>> ConfirmPrizesAsync(
        Guid tournamentId, ConfirmPrizesRequest request, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (tournament.PrizeConfirmed)
            throw new DomainException("Premiação já foi confirmada.");

        // Remover prêmios existentes
        var existing = await _db.TournamentPrizes
            .Where(p => p.TournamentId == tournamentId)
            .ToListAsync(ct);

        foreach (var e in existing)
            _db.TournamentPrizes.Remove(e);

        var totalCosts = await _db.CostExtras
            .Where(c => c.TournamentId == tournamentId)
            .SumAsync(c => c.Amount, ct);

        // Salvar novos prêmios
        var result = new List<PrizeAllocation>();
        foreach (var prize in request.Prizes)
        {
            _db.TournamentPrizes.Add(new TournamentPrize
            {
                TournamentId = tournamentId,
                Position = prize.Position,
                Amount = prize.Amount,
                Percentage = tournament.TotalPrizePool - totalCosts > 0
                    ? (prize.Amount / (tournament.TotalPrizePool - totalCosts)) * 100
                    : 0
            });

            result.Add(new PrizeAllocation
            {
                Position = prize.Position,
                Amount = prize.Amount,
                Percentage = tournament.TotalPrizePool - totalCosts > 0
                    ? Math.Round((prize.Amount / (tournament.TotalPrizePool - totalCosts)) * 100, 1)
                    : 0
            });
        }

        tournament.PrizeConfirmed = true;
        tournament.PrizeConfirmedAt = DateTimeOffset.UtcNow;
        tournament.TotalCosts = totalCosts;
        tournament.NetPrizePool = tournament.TotalPrizePool - totalCosts;

        await _db.SaveChangesAsync(ct);
        return result;
    }

    public static int SuggestPrizeCount(int totalPlayers)
    {
        return totalPlayers switch
        {
            <= 6 => 2,
            <= 10 => 3,
            <= 18 => Math.Max(3, (int)(totalPlayers * 0.25)),
            <= 27 => Math.Max(4, (int)(totalPlayers * 0.22)),
            <= 36 => Math.Max(5, (int)(totalPlayers * 0.20)),
            <= 45 => Math.Max(6, (int)(totalPlayers * 0.18)),
            <= 60 => Math.Max(7, (int)(totalPlayers * 0.17)),
            <= 100 => Math.Max(8, (int)(totalPlayers * 0.15)),
            _ => Math.Max(10, (int)(totalPlayers * 0.12))
        };
    }

    private static decimal[] GetPercentages(int count)
    {
        if (DefaultPercentages.TryGetValue(count, out var pcts))
            return pcts;

        var result = new decimal[count];
        decimal remaining = 1.0m;

        result[0] = 0.26m;
        remaining -= result[0];

        for (int i = 1; i < count; i++)
        {
            var factor = 0.78m;
            if (i == 1) factor = 0.72m;

            result[i] = i == 1
                ? remaining * 0.28m
                : result[i - 1] * factor;
        }

        var sum = result.Sum();
        for (int i = 0; i < count; i++)
            result[i] = Math.Round(result[i] / sum, 4);

        var diff = 1.0m - result.Sum();
        result[0] += diff;

        return result;
    }
}
