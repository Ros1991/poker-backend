namespace PokerTournament.Application.DTOs.Requests;

public class CreateBlindStructureRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? HomeGameId { get; set; }
    public List<BlindLevelItem> Levels { get; set; } = [];

    // Template defaults
    public decimal? DefaultBuyIn { get; set; }
    public decimal? DefaultRebuy { get; set; }
    public decimal? DefaultAddon { get; set; }
    public int? DefaultStartingStack { get; set; }
    public int? DefaultRebuyStack { get; set; }
    public int? DefaultAddonStack { get; set; }
    public int? DefaultMaxRebuys { get; set; }
    public bool? DefaultAddonAllowed { get; set; }
    // SPEC-20261027-1311: máximo de add-ons padrão (0 = ilimitado). POST: null = 1; PUT: null = manter. Negativo → 400.
    public int? DefaultMaxAddons { get; set; }
    public int? DefaultLateRegistrationLevel { get; set; }
    public int? DefaultRebuyUntilLevel { get; set; }
    public int? DefaultSeatsPerTable { get; set; }
    public decimal? DefaultStaffAmount { get; set; }
    public string? DefaultRankingContribMode { get; set; }
    public decimal? DefaultRankingContribValue { get; set; }

    // SPEC-20261023-1215 — custos padrão de valor fixo por torneio (CP1/CP7). No PUT: null/ausente = MANTER a
    // lista atual; lista (inclusive vazia) = SUBSTITUIR tudo. No POST: null = nenhum custo padrão.
    public List<DefaultCostItem>? DefaultCosts { get; set; }

    // Custo de dealer (CP16): null = manter no PUT. DefaultHasDealer = true exige DefaultDealerFee > 0 e
    // <= 99.999,99; false apaga o valor.
    public bool? DefaultHasDealer { get; set; }
    public decimal? DefaultDealerFee { get; set; }

    // Knockout padrão (SPEC-20261026-1909): "None" | "Simple" | "Progressive". No PUT, null = manter os dois campos;
    // no POST, null = Normal. Knockout exige DefaultBountyAmount > 0.
    public string? DefaultKnockoutType { get; set; }
    public decimal? DefaultBountyAmount { get; set; }

    // Timebank padrão (SPEC-20261029-1722): DefaultTimebankCount 0 = sem timebank, 1 a 99 = por jogador; no POST null =
    // sem timebank, no PUT null = manter os dois campos. DefaultTimebankSeconds de 5 a 300 (null = 30 ou o atual).
    public int? DefaultTimebankCount { get; set; }
    public int? DefaultTimebankSeconds { get; set; }

    public class DefaultCostItem
    {
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? Beneficiary { get; set; }
        public string? PixKey { get; set; }
        public string? PixKeyType { get; set; }
    }

    public class BlindLevelItem
    {
        public int LevelNumber { get; set; }
        public int SmallBlind { get; set; }
        public int BigBlind { get; set; }
        public int Ante { get; set; }
        public int BigBlindAnte { get; set; }
        public int DurationMinutes { get; set; }
        public bool IsBreak { get; set; }
        public string? BreakDescription { get; set; }
    }
}
