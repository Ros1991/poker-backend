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
    public string? ResponsiblePixKey { get; set; }
    public decimal? StaffAmount { get; set; }
    public string? RankingContribMode { get; set; }
    public decimal? RankingContribValue { get; set; }

    // SPEC-20261023-1215 (CP17): quantos dealers trabalham no torneio quando a estrutura tem dealer pago.
    // null = 1 (cliente que não conhece o campo); 0 = sem custo de dealer; fora de 0–20 → 400. Não é gravado
    // no torneio: vira o custo "Dealers (N × R$ X)" na criação (e na primeira estrutura de um rascunho).
    public int? DealerCount { get; set; }
}
