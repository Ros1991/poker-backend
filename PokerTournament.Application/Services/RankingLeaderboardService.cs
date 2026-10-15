using System.Text.Json;
using Jint;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Application.Services;

/// <summary>
/// Fonte única do cálculo do leaderboard (telas do admin e página pública).
/// On-the-fly: etapas = torneios Finished do ranking; pontos por tabela, fórmula
/// (Jint) ou fallback; descarte dos N piores com falta valendo 0.
/// </summary>
public class RankingLeaderboardService
{
    // PaidOut também pontua: pagar o prêmio pelo endpoint de pagamento não pode
    // tirar o jogador da etapa (antes ele sumia e o nº de jogadores da etapa caía).
    private static readonly string[] ScoredStatuses =
    [
        nameof(EntryStatus.Eliminated),
        nameof(EntryStatus.Awarded),
        nameof(EntryStatus.PaidOut),
    ];

    private static readonly JsonSerializerOptions TableJsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly IAppDbContext _db;

    public RankingLeaderboardService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<RankingStandings> ComputeAsync(Ranking ranking, CancellationToken ct = default)
    {
        var stages = await _db.Tournaments
            .Where(t => t.RankingId == ranking.Id && t.Status == nameof(TournamentStatus.Finished))
            .Select(t => new { t.Id, t.Name, t.Date })
            .ToListAsync(ct);

        if (stages.Count == 0)
            return new RankingStandings([], 0, []);

        var stageIds = stages.Select(s => s.Id).ToList();

        // Include(Person) de propósito: o filtro global de soft-delete da Person
        // tira da conta entries de pessoas excluídas, como sempre foi.
        var entries = await _db.TournamentEntries
            .Include(e => e.Person)
            .Where(e => stageIds.Contains(e.TournamentId)
                     && e.FinalPosition != null
                     && ScoredStatuses.Contains(e.Status))
            .ToListAsync(ct);

        var playerCounts = entries
            .GroupBy(e => e.TournamentId)
            .ToDictionary(g => g.Key, g => g.Count());

        var table = ParseTable(ranking);

        var scores = entries.Select(e =>
        {
            var position = e.FinalPosition ?? 0;
            var playerCount = playerCounts.GetValueOrDefault(e.TournamentId, 0);
            return new
            {
                Entry = e,
                Position = position,
                Points = CalculatePoints(ranking, table, position, playerCount),
            };
        }).ToList();

        var totalStages = stages.Count;
        var discard = Math.Max(0, ranking.DiscardCount);
        // Slots que contam = etapas - descarte (faltas contam como 0)
        var countedSlots = Math.Max(1, totalStages - discard);
        var stageDates = stages.ToDictionary(s => s.Id, s => s.Date);

        var players = scores
            .GroupBy(s => s.Entry.PersonId)
            .Select(g =>
            {
                // Do melhor para o pior: os countedSlots primeiros contam. As faltas
                // (0) ficariam depois dos reais, então nunca deslocam um resultado real.
                var results = g
                    .OrderByDescending(s => s.Points)
                    .ThenByDescending(s => stageDates.GetValueOrDefault(s.Entry.TournamentId))
                    .Select((s, i) => new RankingStageResult(
                        s.Entry.TournamentId, s.Position, s.Points, i < countedSlots))
                    .ToList();

                var totalPoints = results.Where(r => r.Counted).Sum(r => r.Points);

                return new RankingStanding
                {
                    Person = g.First().Entry.Person,
                    TotalPoints = totalPoints,
                    TournamentsPlayed = g.Count(),
                    BestPosition = g.Min(s => s.Position),
                    DiscardedPoints = results.Sum(r => r.Points) - totalPoints,
                    AveragePoints = g.Count() > 0 ? Math.Round(g.Sum(s => s.Points) / g.Count(), 2) : 0,
                    Results = results,
                };
            })
            // Mesmo critério de sempre. O sort é estável, então empates totais mantêm
            // a ordem em que os jogadores aparecem nas entries (igual ao admin antigo).
            .OrderByDescending(p => p.TotalPoints)
            .ThenByDescending(p => p.AveragePoints)
            .ToList();

        for (var i = 0; i < players.Count; i++)
            players[i].Position = i + 1;

        var stageInfos = stages
            .Select(s => new RankingStageInfo(s.Id, s.Name, s.Date, playerCounts.GetValueOrDefault(s.Id, 0)))
            .ToList();

        return new RankingStandings(stageInfos, countedSlots, players);
    }

    /// <summary>Leaderboard no formato das telas do admin.</summary>
    public async Task<List<RankingLeaderboardResponse>> GetLeaderboardAsync(
        Ranking ranking, CancellationToken ct = default)
    {
        var standings = await ComputeAsync(ranking, ct);

        return standings.Players.Select(p => new RankingLeaderboardResponse
        {
            Position = p.Position,
            Person = new PersonResponse
            {
                Id = p.Person.Id,
                FullName = p.Person.FullName,
                Nickname = p.Person.Nickname,
                PhotoUrl = p.Person.PhotoUrl,
                IsActive = p.Person.IsActive,
            },
            TotalPoints = p.TotalPoints,
            TournamentsPlayed = p.TournamentsPlayed,
            BestPosition = p.BestPosition,
            DiscardedPoints = p.DiscardedPoints,
            AveragePoints = p.AveragePoints,
        }).ToList();
    }

    /// <summary>
    /// Ranking público por slug, ou null se não existe/foi excluído. Monta o DTO
    /// campo a campo: só apelido e foto do jogador, nada de contato ou valores.
    /// </summary>
    public async Task<PublicRankingResponse?> GetPublicBySlugAsync(
        string slug, CancellationToken ct = default)
    {
        var ranking = await _db.Rankings
            .Include(r => r.HomeGame)
            .FirstOrDefaultAsync(r => r.Slug == slug, ct);

        if (ranking is null)
            return null;

        var standings = await ComputeAsync(ranking, ct);

        // Etapas identificadas pela ordem (1..N), NUNCA pelo Id do torneio: com o Id,
        // o endpoint anônimo do telão (display/tournaments/{id}) devolve nome completo
        // e prêmio em R$ de cada premiado.
        var orderedStages = standings.Stages
            .OrderBy(s => s.Date)
            .ThenBy(s => s.TournamentId)
            .ToList();
        var stageNumbers = orderedStages
            .Select((s, i) => (s.TournamentId, Number: i + 1))
            .ToDictionary(x => x.TournamentId, x => x.Number);

        var usesTable = ranking.ScoringMode == "Table" && !string.IsNullOrWhiteSpace(ranking.ScoringTable);

        return new PublicRankingResponse
        {
            Slug = ranking.Slug!,
            Name = ranking.Name,
            Season = ranking.Season,
            HomeGameName = ranking.HomeGame.Name,
            AccumulatedPrize = ranking.AccumulatedPrize,
            DiscardCount = Math.Max(0, ranking.DiscardCount),
            TotalStages = standings.Stages.Count,
            CountedSlots = standings.CountedSlots,
            // Sem fórmula vale o fallback (jogadores - posição + 1), que também usa o campo
            PointsDependOnFieldSize = !usesTable
                && (string.IsNullOrWhiteSpace(ranking.ScoringFormula)
                    || ranking.ScoringFormula.Contains("jogadores", StringComparison.Ordinal)),
            Stages = orderedStages
                .Select(s => new PublicRankingStageResponse
                {
                    Number = stageNumbers[s.TournamentId],
                    Name = s.Name,
                    Date = s.Date,
                    PlayerCount = s.PlayerCount,
                })
                .ToList(),
            Leaderboard = standings.Players.Select(p => new PublicRankingPlayerResponse
            {
                Position = p.Position,
                PersonId = p.Person.Id,
                DisplayName = PublicName(p.Person),
                PhotoUrl = p.Person.PhotoUrl,
                TotalPoints = p.TotalPoints,
                TournamentsPlayed = p.TournamentsPlayed,
                BestPosition = p.BestPosition,
                DiscardedPoints = p.DiscardedPoints,
                AveragePoints = p.AveragePoints,
                Results = p.Results.Select(r => new PublicRankingResultResponse
                {
                    Stage = stageNumbers[r.TournamentId],
                    Position = r.Position,
                    Points = r.Points,
                    Counted = r.Counted,
                }).ToList(),
            }).ToList(),
            GeneratedAt = DateTimeOffset.UtcNow,
        };
    }

    // Apelido; sem apelido, primeiro nome + inicial do último sobrenome
    // ("João Carlos da Silva" → "João S."), nunca o nome completo.
    private static string PublicName(Person person)
    {
        if (!string.IsNullOrWhiteSpace(person.Nickname))
            return person.Nickname;

        var parts = person.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "Jogador",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }

    private static List<PositionPoints>? ParseTable(Ranking ranking)
    {
        if (ranking.ScoringMode != "Table" || string.IsNullOrWhiteSpace(ranking.ScoringTable))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<PositionPoints>>(ranking.ScoringTable, TableJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static decimal CalculatePoints(
        Ranking ranking, List<PositionPoints>? table, int position, int playerCount)
    {
        if (ranking.ScoringMode == "Table" && table != null)
        {
            var row = table.FirstOrDefault(p => p.Position == position);
            return row?.Points ?? 0m;
        }

        // Fórmula JavaScript do ranking. Os limites só cortam fórmula com laço ou
        // recursão (o endpoint é anônimo); uma expressão normal fica muito abaixo.
        // Sem TimeoutInterval: ele cria um timer por avaliação e, no cold start,
        // poderia estourar e cair calado no fallback com pontos errados.
        if (!string.IsNullOrWhiteSpace(ranking.ScoringFormula))
        {
            try
            {
                var engine = new Engine(options => options
                    .MaxStatements(10_000)
                    .LimitRecursion(64)
                    .RegexTimeoutInterval(TimeSpan.FromMilliseconds(500)));
                engine.SetValue("posicao", position);
                engine.SetValue("jogadores", playerCount);
                var result = engine.Evaluate(ranking.ScoringFormula);
                var value = Convert.ToDecimal(result.ToObject());
                return Math.Max(0, Math.Round(value, 2));
            }
            catch
            {
                // fallback abaixo
            }
        }

        return Math.Max(0, playerCount - position + 1);
    }

    private sealed class PositionPoints
    {
        public int Position { get; set; }
        public decimal Points { get; set; }
    }
}

public sealed record RankingStandings(
    IReadOnlyList<RankingStageInfo> Stages,
    int CountedSlots,
    IReadOnlyList<RankingStanding> Players);

public sealed record RankingStageInfo(Guid TournamentId, string Name, DateTimeOffset Date, int PlayerCount);

public sealed record RankingStageResult(Guid TournamentId, int Position, decimal Points, bool Counted);

public sealed class RankingStanding
{
    public int Position { get; set; }
    public required Person Person { get; init; }
    public decimal TotalPoints { get; init; }
    public int TournamentsPlayed { get; init; }
    public int? BestPosition { get; init; }
    public decimal DiscardedPoints { get; init; }
    public decimal AveragePoints { get; init; }
    public required IReadOnlyList<RankingStageResult> Results { get; init; }
}
