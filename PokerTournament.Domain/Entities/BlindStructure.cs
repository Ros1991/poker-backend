namespace PokerTournament.Domain.Entities;

public class BlindStructure : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? HomeGameId { get; set; }
    public bool IsActive { get; set; } = true;

    // Template defaults (pre-fill tournament when selecting this structure)
    public decimal? DefaultBuyIn { get; set; }
    public decimal? DefaultRebuy { get; set; }
    public decimal? DefaultAddon { get; set; }
    public int? DefaultStartingStack { get; set; }
    public int? DefaultRebuyStack { get; set; }
    public int? DefaultAddonStack { get; set; }
    public int? DefaultMaxRebuys { get; set; }
    public bool DefaultAddonAllowed { get; set; } = true;
    // Máximo de add-ons por jogador padrão (SPEC-20261027-1311): 0 = ilimitado; padrão 1 (o torneio herda)
    public int DefaultMaxAddons { get; set; } = 1;
    public int? DefaultLateRegistrationLevel { get; set; }
    public int? DefaultRebuyUntilLevel { get; set; }
    public int? DefaultSeatsPerTable { get; set; }

    // Staff e contribuição p/ ranking (pré-preenchem o torneio ao selecionar a estrutura)
    public decimal? DefaultStaffAmount { get; set; }
    public string? DefaultRankingContribMode { get; set; } // PerPlayer | Percent
    public decimal? DefaultRankingContribValue { get; set; }

    // Custo de dealer (SPEC-20261023-1215, CP16): valor POR DEALER, fixo por torneio. A criação do torneio
    // pergunta a quantidade e lança "Dealers (N × R$ X)". Não substitui o Staff (que é por jogador/entrada).
    public bool DefaultHasDealer { get; set; }
    public decimal? DefaultDealerFee { get; set; }

    // Knockout padrão (SPEC-20261026-1909): o torneio criado com a estrutura herda o formato e o valor do bounty
    // (editáveis no torneio). None = Normal. Em qualquer plano.
    public string DefaultKnockoutType { get; set; } = nameof(Enums.KnockoutType.None);
    public decimal? DefaultBountyAmount { get; set; }

    // Timebank padrão (SPEC-20261029-1722): o torneio criado com a estrutura herda (editável no torneio). Null = sem
    // timebank; ligado, quantidade de 1 a 99 e segundos de 5 a 300.
    public int? DefaultTimebankCount { get; set; }
    public int? DefaultTimebankSeconds { get; set; }

    // Navigation
    public HomeGame? HomeGame { get; set; }
    public ICollection<BlindLevel> Levels { get; set; } = [];
    /// <summary>Custos padrão de valor fixo copiados para cada torneio criado com a estrutura (CP1).</summary>
    public ICollection<BlindStructureDefaultCost> DefaultCosts { get; set; } = [];
}
