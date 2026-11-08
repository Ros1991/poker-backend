namespace PokerTournament.Domain.Enums;

/// <summary>
/// Quem vê o perfil público completo da conta (SPEC-20261108-1636). Gravado como string via nameof.
/// Everyone = qualquer conta logada; Friends = só amigos (SPEC-20261108-1637; até lá ninguém além do dono);
/// Nobody = terceiros veem só foto e apelido.
/// </summary>
public enum ProfileVisibility
{
    Everyone,
    Friends,
    Nobody
}
