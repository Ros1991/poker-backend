namespace PokerTournament.Domain.Enums;

/// <summary>
/// Com que precisão as coordenadas do home game foram achadas (SPEC-20261108-1634). O geocodificador tenta do mais
/// preciso ao menos preciso e guarda o nível que respondeu, como string via <c>nameof</c> em
/// <c>home_games.geocode_precision</c>.
/// </summary>
public enum GeocodePrecision
{
    /// <summary>"rua, número, cidade, UF": o ponto do endereço.</summary>
    Address,
    /// <summary>"rua, cidade, UF": um ponto da rua (sem o número).</summary>
    Street,
    /// <summary>Só o CEP: o centro da área do CEP.</summary>
    PostalCode,
}
