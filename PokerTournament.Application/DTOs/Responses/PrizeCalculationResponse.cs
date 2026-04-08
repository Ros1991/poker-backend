namespace PokerTournament.Application.DTOs.Responses;

public class PrizeCalculationResponse
{
    public decimal GrossPrizePool { get; set; }
    public decimal TotalCosts { get; set; }
    public decimal NetPrizePool { get; set; }
    public int SuggestedPrizeCount { get; set; }
    public List<PrizeAllocation> Prizes { get; set; } = [];
    public decimal Remainder { get; set; }
}

public class PrizeAllocation
{
    public int Position { get; set; }
    public decimal Amount { get; set; }
    public decimal Percentage { get; set; }
}
