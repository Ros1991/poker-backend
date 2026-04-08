namespace PokerTournament.Application.DTOs.Requests;

public class CreateTournamentRequest
{
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public Guid? RankingId { get; set; }
    public Guid? BlindStructureId { get; set; }
    public decimal BuyInAmount { get; set; }
    public decimal? RebuyAmount { get; set; }
    public decimal? AddonAmount { get; set; }
    public int StartingStack { get; set; }
    public int? RebuyStack { get; set; }
    public int? AddonStack { get; set; }
    public int? MaxRebuys { get; set; }
    public bool AddonAllowed { get; set; }
    public bool AddonDoubleAllowed { get; set; }
    public int? LateRegistrationLevel { get; set; }
    public int? RebuyUntilLevel { get; set; }
    public int SeatsPerTable { get; set; } = 9;
}
