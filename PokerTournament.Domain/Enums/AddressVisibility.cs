namespace PokerTournament.Domain.Enums;

/// <summary>
/// Quem vê o endereço completo do home game (SPEC-20261108-1634, D6 do plano da rede do jogador). Gravado como string
/// via <c>nameof</c> em <c>home_games.address_visibility</c>. Quem gerencia o home game vê tudo sempre; cidade, UF e
/// bairro aparecem para qualquer um que acesse o home game; as coordenadas nunca saem para quem não gerencia.
/// </summary>
public enum AddressVisibility
{
    /// <summary>Padrão: rua, número e complemento só para quem gerencia ou foi aprovado no torneio (SPEC-20261108-1639).</summary>
    ApprovedOnly,
    /// <summary>Endereço completo para qualquer um que veja o home game ou o torneio público.</summary>
    Public,
}
