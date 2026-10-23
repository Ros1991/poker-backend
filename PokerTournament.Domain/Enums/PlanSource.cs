namespace PokerTournament.Domain.Enums;

/// <summary>
/// De onde veio o plano da conta. Manual = atribuído pelo suporte; Cortesia = presente (ex.: "fundador");
/// Trial = período de teste (previsto para quando a cobrança entrar — D11); Assinatura = cobrança automática.
/// </summary>
public enum PlanSource
{
    Manual,
    Cortesia,
    Trial,
    Assinatura
}
