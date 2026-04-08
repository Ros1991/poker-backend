namespace PokerTournament.Application.DTOs.Requests;

public class ConfirmPrizesRequest
{
    public List<PrizeItem> Prizes { get; set; } = [];

    public class PrizeItem
    {
        public int Position { get; set; }
        public decimal Amount { get; set; }
    }
}
