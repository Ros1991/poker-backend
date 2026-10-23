namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// Ranking para a página pública (sem login). Só dados públicos: apelido e foto
/// do jogador; nada de contato, documento ou valores por jogador.
/// </summary>
public class PublicRankingResponse
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Season { get; set; }
    public string HomeGameName { get; set; } = string.Empty;
    public decimal AccumulatedPrize { get; set; }
    public int DiscardCount { get; set; }
    public int TotalStages { get; set; }
    public int CountedSlots { get; set; }
    public bool PointsDependOnFieldSize { get; set; } // false: ranking por tabela (só a posição importa)
    public List<PublicRankingStageResponse> Stages { get; set; } = [];
    public List<PublicRankingPlayerResponse> Leaderboard { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; }

    // SPEC-20261023-1205: mesma regra de selo/logo do telão (plano efetivo do dono do home game)
    public string Plan { get; set; } = "Mesa";
    public bool ShowBadge { get; set; } = true;
    public string? ClubLogoUrl { get; set; } // só Liga
}

// Etapa identificada pela ordem cronológica (1..N), não pelo Id do torneio
public class PublicRankingStageResponse
{
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public int PlayerCount { get; set; }
}

public class PublicRankingPlayerResponse
{
    public int Position { get; set; }
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public decimal TotalPoints { get; set; }
    public int TournamentsPlayed { get; set; }
    public int? BestPosition { get; set; }
    public decimal DiscardedPoints { get; set; }
    public decimal AveragePoints { get; set; }
    public List<PublicRankingResultResponse> Results { get; set; } = [];
}

public class PublicRankingResultResponse
{
    public int Stage { get; set; } // PublicRankingStageResponse.Number
    public int Position { get; set; }
    public decimal Points { get; set; }
    public bool Counted { get; set; }
}
