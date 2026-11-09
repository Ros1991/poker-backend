namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// <c>GET me/tournaments/near</c> (SPEC-20261108-1639): torneios públicos futuros num raio da localização da conta — a do
/// navegador (cabeçalho <c>X-Visitor-Location</c>, usada só nesta resposta) ou a cidade/UF do perfil geocodificada. Sem
/// nenhuma das duas, <see cref="LocationSource"/> = "none" e a tela pede para completar o perfil. O raio e a distância
/// são medidos até o PONTO PÚBLICO de cada clube (grade de 0,02°), em km inteiros.
/// </summary>
public class NearbyTournamentsResponse
{
    /// <summary>"browser" | "profile" | "none".</summary>
    public string LocationSource { get; set; } = "none";
    /// <summary>Texto da origem ("Sua localização" ou "Campinas - SP"); null em "none".</summary>
    public string? LocationLabel { get; set; }
    /// <summary>A cidade do perfil não foi achada pelo geocodificador (ou ele está fora do ar): sem resultados.</summary>
    public bool GeocodeFailed { get; set; }
    public double RadiusKm { get; set; }
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<NearbyTournamentItem> Items { get; set; } = [];
}

public class NearbyTournamentItem
{
    public Guid TournamentId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClubName { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    /// <summary>"Bairro, Cidade - UF" (com a rua e o número quando o dono liberou o endereço do home game).</summary>
    public string? LocationSummary { get; set; }
    /// <summary>Km inteiros (mínimo 1) até o ponto público do clube.</summary>
    public int DistanceKm { get; set; }
    public DateTimeOffset Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal BuyInAmount { get; set; }
    public decimal? RebuyAmount { get; set; }
    public decimal? AddonAmount { get; set; }
    /// <summary>None | Simple | Progressive.</summary>
    public string KnockoutType { get; set; } = "None";
    public int? MaxPlayers { get; set; }
    public int? SpotsLeft { get; set; }
    /// <summary>Torneio de um clube que a conta acessa (dona ou membro): marcado "seu clube".</summary>
    public bool IsMyClub { get; set; }
    /// <summary>Pedido aberto da conta neste torneio (Pending | Approved | Waitlisted), ou null.</summary>
    public string? MyRequestStatus { get; set; }
    /// <summary>A conta já está inscrita (jogador vinculado).</summary>
    public bool Registered { get; set; }
}
