using PokerTournament.Application.Geo;

namespace PokerTournament.Application.DTOs.Requests;

public class CreateHomeGameRequest : IHomeGameAddressInput
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Ponto de referência (texto livre), opcional desde o endereço estruturado (SPEC-20261108-1634).</summary>
    public string? Location { get; set; }
    public string? PixKey { get; set; }
    // SPEC-20261023-1218: tipo escolhido da chave (null = detectar); a chave é normalizada no HomeGamesController
    public string? PixKeyType { get; set; }
    public string? PixBeneficiary { get; set; }
    public decimal? DefaultBuyIn { get; set; }
    public decimal? DefaultRebuy { get; set; }
    public decimal? DefaultAddon { get; set; }

    // Endereço estruturado (SPEC-20261108-1634): o PUT copia o pedido inteiro, então a tela manda sempre os campos
    // gravados; HomeGameAddress.Apply normaliza, valida e zera as coordenadas quando o lugar muda.
    /// <summary>CEP com ou sem máscara (8 dígitos).</summary>
    public string? PostalCode { get; set; }
    public string? Street { get; set; }
    public string? Number { get; set; }
    public string? Complement { get; set; }
    /// <summary>Bairro.</summary>
    public string? District { get; set; }
    public string? City { get; set; }
    /// <summary>UF (2 letras).</summary>
    public string? State { get; set; }
    /// <summary>"ApprovedOnly" | "Public"; ausente = mantém (padrão ApprovedOnly no novo).</summary>
    public string? AddressVisibility { get; set; }
}
