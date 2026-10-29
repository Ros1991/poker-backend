using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Plans;

/// <summary>
/// Recursos exclusivos de plano pago (SPEC-20261023-1205, tabela 3.1 do plano). O nome do valor
/// é o que vai nas respostas (<c>planFeatures</c>, <c>features</c>) e no campo <c>feature</c> do 402.
/// </summary>
public enum PlanFeature
{
    /// <summary>Controle de mesas: sorteio, assentos, mover, quebrar.</summary>
    Tables,
    /// <summary>Premiação: cálculo, confirmação, prêmio pago.</summary>
    Prizes,
    /// <summary>Marcar inscrição como paga, abatimento, estorno, sugestões de PIX, PIX do dia.</summary>
    Payments,
    /// <summary>Custos extras do torneio.</summary>
    CostExtras,
    /// <summary>Financeiro consolidado (totais, caixa).</summary>
    Financial,
    /// <summary>Ranking de temporada.</summary>
    Rankings,
    /// <summary>Terminal do dealer por QR.</summary>
    DealerQr,
    /// <summary>Card PIX com QR no telão.</summary>
    PixCardOnTv,
    /// <summary>Logo do clube no telão e no ranking público (só Liga).</summary>
    ClubLogoOnTv,
}

/// <summary>
/// Um degrau do catálogo. Serializado como está em <c>GET public/plans</c> e no <c>catalog</c> de
/// <c>GET me/plan</c> — a ÚNICA fonte de preços e limites do frontend (landing, boas-vindas, Meu plano, modal).
/// </summary>
public sealed class PlanDefinition
{
    public string Name { get; init; } = string.Empty;
    public int Order { get; init; }
    public decimal PriceMonthly { get; init; }
    /// <summary>Só exibido ("quando a cobrança entrar"); nada cobra hoje.</summary>
    public decimal PriceYearly { get; init; }
    /// <summary>
    /// Home games como dono CONTANDO o principal da conta (SPEC-20261029-1723): Mesa 1, Clube 1, Liga 3 (o principal + 2
    /// que podem ser criados e excluídos). O principal existe em todo plano; criar só passa enquanto os outros (não
    /// principais) cabem em <c>MaxOwnedHomeGames - 1</c> — no Mesa e no Clube, nenhum.
    /// </summary>
    public int MaxOwnedHomeGames { get; init; }
    /// <summary>
    /// "Home game" aparece como conceito na interface (seletor, criar, excluir)? Só na Liga (D18 do plano de
    /// 2026-10-29): no Mesa e no Clube a conta tem um só, invisível — a conta é o clube.
    /// </summary>
    public bool HomeGamesVisible { get; init; }
    /// <summary>Membros além do dono; null = ilimitado.</summary>
    public int? MaxMembersPerHomeGame { get; init; }
    /// <summary>Torneios criados numa janela móvel de 7 dias; null = ilimitado.</summary>
    public int? TournamentsPer7Days { get; init; }
    public IReadOnlyList<string> Features { get; init; } = [];
    /// <summary>Selo "Feito com Poker da Galera" no telão e no ranking público.</summary>
    public bool ShowBadge { get; init; }
    /// <summary>Selo "Mais escolhido" na tabela comparativa (Clube).</summary>
    public bool Highlight { get; init; }

    public bool Has(PlanFeature feature) => Features.Contains(feature.ToString());
}

/// <summary>
/// Catálogo de planos em código (até a cobrança entrar): tabela 3.1 de docs/PLANO-poker-da-galera-produto.md.
/// Mesa R$ 0 / Clube R$ 49,90 / Liga R$ 99,90 por mês (anual R$ 499 / R$ 999 só exibido); home games
/// como dono 1 / 1 / 3 contando o principal da conta (SPEC-20261029-1723: Mesa e Clube só têm o principal, invisível;
/// a Liga cria mais 2); torneios 1 a cada 7 dias / ilimitado / ilimitado; membros além do dono 0 / 3 / ilimitado.
/// </summary>
public static class PlanCatalog
{
    public const string Mesa = nameof(Domain.Enums.Plan.Mesa);
    public const string Clube = nameof(Domain.Enums.Plan.Clube);
    public const string Liga = nameof(Domain.Enums.Plan.Liga);

    /// <summary>Janela móvel do limite de torneios.</summary>
    public static readonly TimeSpan TournamentWindow = TimeSpan.FromDays(7);

    private static readonly string[] PaidFeatures =
    [
        nameof(PlanFeature.Tables),
        nameof(PlanFeature.Prizes),
        nameof(PlanFeature.Payments),
        nameof(PlanFeature.CostExtras),
        nameof(PlanFeature.Financial),
        nameof(PlanFeature.Rankings),
        nameof(PlanFeature.DealerQr),
        nameof(PlanFeature.PixCardOnTv),
    ];

    /// <summary>Todos os recursos (o que vale quando Plans:Enforce = false).</summary>
    public static readonly IReadOnlyList<string> AllFeatures = Enum.GetNames<PlanFeature>();

    public static readonly IReadOnlyList<PlanDefinition> All =
    [
        new PlanDefinition
        {
            Name = Mesa,
            Order = 0,
            PriceMonthly = 0m,
            PriceYearly = 0m,
            MaxOwnedHomeGames = 1,
            HomeGamesVisible = false,
            MaxMembersPerHomeGame = 0,
            TournamentsPer7Days = 1,
            Features = [],
            ShowBadge = true,
            Highlight = false,
        },
        new PlanDefinition
        {
            Name = Clube,
            Order = 1,
            PriceMonthly = 49.90m,
            PriceYearly = 499m,
            MaxOwnedHomeGames = 1,
            HomeGamesVisible = false,
            MaxMembersPerHomeGame = 3,
            TournamentsPer7Days = null,
            Features = PaidFeatures,
            ShowBadge = true,
            Highlight = true,
        },
        new PlanDefinition
        {
            Name = Liga,
            Order = 2,
            PriceMonthly = 99.90m,
            PriceYearly = 999m,
            MaxOwnedHomeGames = 3,
            HomeGamesVisible = true,
            MaxMembersPerHomeGame = null,
            TournamentsPer7Days = null,
            Features = [.. PaidFeatures, nameof(PlanFeature.ClubLogoOnTv)],
            ShowBadge = false,
            Highlight = false,
        },
    ];

    /// <summary>Definição do plano pelo nome (sem distinguir maiúsculas); nome desconhecido vale Mesa.</summary>
    public static PlanDefinition Get(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>Nome canônico do plano, ou null se não existir no catálogo.</summary>
    public static string? Normalize(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase))?.Name;

    /// <summary>
    /// Plano efetivo: <paramref name="plan"/> se <paramref name="expiresAt"/> for nulo ou futuro; senão Mesa.
    /// Calculado na leitura (sem job). Plano desconhecido vale Mesa.
    /// </summary>
    public static string EffectivePlan(string? plan, DateTimeOffset? expiresAt)
    {
        if (expiresAt is { } exp && exp <= DateTimeOffset.UtcNow)
            return Mesa;
        return Normalize(plan) ?? Mesa;
    }

    public static string EffectivePlan(User user) => EffectivePlan(user.Plan, user.PlanExpiresAt);

    /// <summary>Plano mais barato que tem o recurso.</summary>
    public static string MinimumPlanFor(PlanFeature feature) =>
        All.OrderBy(p => p.Order).First(p => p.Has(feature)).Name;

    /// <summary>Nome do recurso como aparece na mensagem do 402.</summary>
    public static string FeatureLabel(PlanFeature feature) => feature switch
    {
        PlanFeature.Tables => "Controle de mesas",
        PlanFeature.Prizes => "Premiação",
        PlanFeature.Payments => "Controle de pagamentos",
        PlanFeature.CostExtras => "Controle de custos",
        PlanFeature.Financial => "Financeiro do torneio",
        PlanFeature.Rankings => "Ranking de temporada",
        PlanFeature.DealerQr => "Terminal do dealer por QR",
        PlanFeature.PixCardOnTv => "Card PIX no telão",
        PlanFeature.ClubLogoOnTv => "Logo do clube no telão",
        _ => feature.ToString(),
    };

    /// <summary>"Controle de mesas está disponível a partir do plano Clube."</summary>
    public static string FeatureLockedMessage(PlanFeature feature)
    {
        var required = MinimumPlanFor(feature);
        return required == Liga
            ? $"{FeatureLabel(feature)} está disponível no plano {Liga}."
            : $"{FeatureLabel(feature)} está disponível a partir do plano {required}.";
    }

    /// <summary>Preenche os campos de plano do <see cref="UserResponse"/> a partir da conta.</summary>
    public static void FillUserPlan(UserResponse response, User user)
    {
        response.Plan = Normalize(user.Plan) ?? Mesa;
        response.EffectivePlan = EffectivePlan(user);
        response.PlanExpiresAt = user.PlanExpiresAt;
        response.PlanSource = user.PlanSource;
        response.PlanNote = user.PlanNote;
    }
}
