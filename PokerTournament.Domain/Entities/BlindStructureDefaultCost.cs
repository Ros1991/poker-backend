namespace PokerTournament.Domain.Entities;

/// <summary>
/// Custo padrão de valor fixo por torneio guardado na estrutura de blinds (SPEC-20261023-1215, CP1):
/// ex.: "Churrasqueiro — R$ 330". É copiado como <see cref="CostExtra"/> (CostType = Default) para todo
/// torneio criado com a estrutura; depois da cópia pertence ao torneio (mudar a estrutura não mexe nele).
/// Apagado junto com a estrutura (cascata).
/// </summary>
public class BlindStructureDefaultCost : BaseEntity
{
    public Guid BlindStructureId { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>Valor fixo por torneio (&gt; 0).</summary>
    public decimal Amount { get; set; }
    /// <summary>"Para quem" (opcional).</summary>
    public string? Beneficiary { get; set; }
    public string? PixKey { get; set; }
    /// <summary>Sem campo na tela (o sistema não pede tipo de chave em lugar nenhum); só repassado.</summary>
    public string? PixKeyType { get; set; }
    /// <summary>Posição na lista da estrutura (0, 1, 2…).</summary>
    public int SortOrder { get; set; }

    // Navigation
    public BlindStructure BlindStructure { get; set; } = null!;
}
