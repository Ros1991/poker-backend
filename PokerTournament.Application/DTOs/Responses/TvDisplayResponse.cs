namespace PokerTournament.Application.DTOs.Responses;

public class TvDisplayResponse
{
    public TvTournamentInfo Tournament { get; set; } = new();
    public TimerStateResponse Blinds { get; set; } = new();
    public TvStats Stats { get; set; } = new();
    public List<TvTableInfo> Tables { get; set; } = [];
    public List<TvEliminationInfo> RecentEliminations { get; set; } = [];
    public List<PrizeAllocation> Prizes { get; set; } = [];
    public List<TvInTheMoneyInfo> InTheMoney { get; set; } = [];
}

public class TvTournamentInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
}

public class TvStats
{
    public int TotalEntries { get; set; }
    public int PlayersRemaining { get; set; }
    public int TotalRebuys { get; set; }
    public int TotalAddons { get; set; }
    public decimal TotalPrizePool { get; set; }
    public decimal AverageStack { get; set; }
}

public class TvTableInfo
{
    public Guid Id { get; set; }
    public int TableNumber { get; set; }
    public string? TableName { get; set; }
    public int PlayerCount { get; set; }
    public int MaxSeats { get; set; }
}

public class TvEliminationInfo
{
    public string PlayerName { get; set; } = string.Empty;
    public string? PlayerPhoto { get; set; }
    public int Position { get; set; }
    public string? EliminatedByName { get; set; }
    public DateTimeOffset EliminatedAt { get; set; }
}

public class TvInTheMoneyInfo
{
    public string PlayerName { get; set; } = string.Empty;
    public string? PlayerPhoto { get; set; }
    public int Position { get; set; }
    public decimal PrizeAmount { get; set; }
}
