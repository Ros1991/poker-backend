namespace PokerTournament.Domain.Enums;

/// <summary>
/// Plano da conta (SPEC-20261023-1205). Persistido como string via nameof(...), como User.Role.
/// Mesa = gratuito (só controla o jogo); Clube = home game semanal completo; Liga = até 3 clubes,
/// membros ilimitados e logo do clube no telão. Limites e preços ficam no PlanCatalog (Application).
/// </summary>
public enum Plan
{
    Mesa,
    Clube,
    Liga
}
