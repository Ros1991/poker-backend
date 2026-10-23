namespace PokerTournament.Domain.Exceptions;

/// <summary>
/// Recusa por plano (SPEC-20261023-1205). O ErrorHandlingMiddleware traduz em HTTP 402 com o corpo
/// único <c>{ status, error: "PaymentRequired", code, feature, currentPlan, requiredPlan, usage, limit,
/// nextAllowedAt, message, timestamp }</c> — é o que o UpgradeModal do frontend lê. Herda de
/// <see cref="DomainException"/> (é regra de negócio), então o middleware precisa tratá-la ANTES
/// do caso genérico de DomainException (senão vira 400).
/// </summary>
public class PlanLimitException : DomainException
{
    /// <summary>Recurso exclusivo de plano pago (mesas, premiação, pagamentos, custos, ranking, dealer por QR).</summary>
    public const string FeatureLocked = "feature_locked";
    /// <summary>Limite de home games como dono.</summary>
    public const string HomeGamesLimit = "home_games_limit";
    /// <summary>Limite de torneios criados numa janela móvel de 7 dias.</summary>
    public const string TournamentsPerWeek = "tournaments_per_week";
    /// <summary>Limite de membros além do dono num home game.</summary>
    public const string MembersLimit = "members_limit";

    public string Code { get; }
    /// <summary>Nome do PlanFeature (ex.: "Tables"); null nos limites de quantidade.</summary>
    public string? Feature { get; }
    /// <summary>Plano efetivo de quem esbarrou (o dono do home game/torneio).</summary>
    public string CurrentPlan { get; }
    /// <summary>Plano mais barato que libera a ação.</summary>
    public string RequiredPlan { get; }
    public int? Usage { get; }
    public int? Limit { get; }
    /// <summary>Quando a próxima criação fica liberada (só em tournaments_per_week).</summary>
    public DateTimeOffset? NextAllowedAt { get; }

    public PlanLimitException(
        string code,
        string message,
        string currentPlan,
        string requiredPlan,
        string? feature = null,
        int? usage = null,
        int? limit = null,
        DateTimeOffset? nextAllowedAt = null)
        : base(message)
    {
        Code = code;
        Feature = feature;
        CurrentPlan = currentPlan;
        RequiredPlan = requiredPlan;
        Usage = usage;
        Limit = limit;
        NextAllowedAt = nextAllowedAt;
    }
}
