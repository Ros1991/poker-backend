namespace PokerTournament.Domain.Enums;

/// <summary>
/// Provedor de login social (SPEC-20261023-1210). Persistido como string via nameof(...) em
/// <c>user_logins.provider</c>. Só o Google está ligado hoje; a Apple fica pronta no modelo e
/// desligada por configuração (D17 — <c>Auth:Apple:ClientId</c> vazio).
/// </summary>
public enum ExternalLoginProvider
{
    Google,
    Apple
}
