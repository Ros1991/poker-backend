namespace PokerTournament.Application.DTOs.Responses;

/// <summary>TV recém-criada: o token do aparelho só aparece aqui, uma vez.</summary>
public class DisplaySessionCreatedResponse
{
    public string DeviceToken { get; set; } = string.Empty;
    public DisplaySessionStatusResponse Session { get; set; } = new();
}

/// <summary>O que a TV precisa saber a cada consulta.</summary>
public class DisplaySessionStatusResponse
{
    public string Status { get; set; } = string.Empty; // Pending | Paired
    public string? PairingCode { get; set; }           // sempre válido: renovado sozinho
    public DateTimeOffset? CodeExpiresAt { get; set; }
    public Guid? TournamentId { get; set; }
    public string? TournamentName { get; set; }
}

/// <summary>Celular conferindo o código lido/digitado antes de escolher o torneio.</summary>
public class PairingCodeLookupResponse
{
    public bool Valid { get; set; }
    public string? CurrentTournamentName { get; set; } // a TV já mostra este (troca)
}

public class PairableTournamentResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public string HomeGameName { get; set; } = string.Empty;
}

public class DisplayPairedResponse
{
    public Guid TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
}

public class DisplayDeviceTokenResponse
{
    public string DeviceToken { get; set; } = string.Empty;
}

/// <summary>
/// Tudo que o telão mostra, numa resposta só e sem dados sensíveis: jogador só
/// pelo nome de exibição (apelido), sem contato, documento, notas ou custos.
/// </summary>
public class TvSnapshotResponse
{
    public TvTournamentInfo Tournament { get; set; } = new();
    public TimerStateResponse? Timer { get; set; }
    public List<TvBlindLevel> BlindLevels { get; set; } = [];
    public List<TvEntry> Entries { get; set; } = [];
    public List<TvTable> Tables { get; set; } = [];
    public List<TvPrize> Prizes { get; set; } = [];
    public decimal PrizePool { get; set; } // confirmada: soma dos prêmios; senão: arrecadado - custos
    public DateTimeOffset GeneratedAt { get; set; }
}

public class TvTournamentInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool PrizeConfirmed { get; set; }
    public int StartingStack { get; set; }
    public int? RebuyStack { get; set; }
    public int? AddonStack { get; set; }
    public int? PunctualityBonusCount { get; set; }
    public int? PunctualityBonusChips { get; set; }
    public int TotalEntries { get; set; }
    public int TotalRebuys { get; set; }
    public int TotalAddons { get; set; }
    public string? ResponsiblePixKey { get; set; } // PIX do dia, já exibido em QR no telão (null no Mesa)

    // Knockout (SPEC-20261026-1909, todos os planos): selo do formato no telão e, no PKO, a cabeça de cada jogador
    public string KnockoutType { get; set; } = "None";
    public decimal? BountyAmount { get; set; }
    public decimal TotalBounties { get; set; }

    // SPEC-20261023-1205: plano efetivo do dono e o selo. Mesa = selo + QR da landing; Clube = selo
    // discreto; Liga = sem selo, com a logo do clube (HomeGame.LogoUrl). No Mesa o snapshot já vem sem
    // prêmios, mesas, PIX e saldos — o backend é a fonte, não o CSS.
    public string Plan { get; set; } = "Mesa";
    public bool ShowBadge { get; set; } = true;
    public string? ClubLogoUrl { get; set; } // só Liga
    /// <summary>
    /// Recursos que valem no torneio (os mesmos do TournamentResponse.planFeatures; com Plans:Enforce = false, todos).
    /// O telão decide cada bloco por aqui — Payments: coluna "Deve" e card PIX; Prizes: premiação; Tables: visão de
    /// mesas —, e não pelo nome do plano: Plan/ShowBadge/ClubLogoUrl ficam só para o selo.
    /// </summary>
    public List<string> PlanFeatures { get; set; } = [];
}

public class TvBlindLevel
{
    public int LevelNumber { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsBreak { get; set; }
}

public class TvEntry
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? FinalPosition { get; set; }
    public Guid? TableId { get; set; }
    public int? SeatNumber { get; set; }
    public int RebuyCount { get; set; }
    // Add-ons comprados (SPEC-20261027-1311; o antigo duplo = 2): fichas em jogo = AddonCount × fichas do add-on
    public int AddonCount { get; set; }
    public bool AddonPurchased => AddonCount > 0;
    public decimal Balance { get; set; }
    public decimal TotalDue { get; set; }
    public decimal? PrizeAmount { get; set; }
    // Knockout: cabeça atual (0 depois de eliminado) e bounties ganhos
    public decimal BountyHead { get; set; }
    public decimal BountyWon { get; set; }
}

public class TvTable
{
    public Guid Id { get; set; }
    public int TableNumber { get; set; }
    public string? TableName { get; set; }
    public int MaxSeats { get; set; }
    public string? DealerName { get; set; }
    public List<TvSeat> Players { get; set; } = [];
}

public class TvSeat
{
    public int? SeatNumber { get; set; }
    public Guid PersonId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TvPrize
{
    public int Position { get; set; }
    public decimal Amount { get; set; }
}
