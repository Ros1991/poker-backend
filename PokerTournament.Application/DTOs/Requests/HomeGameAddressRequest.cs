using PokerTournament.Application.Geo;

namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// PUT HomeGames/{id}/address (SPEC-20261108-1634): só o endereço, sem tocar no resto do home game. É o que a seção
/// "Onde você joga" do Meu perfil (plano Mesa) usa — lá não existe Configurações nem o formulário inteiro.
/// </summary>
public class HomeGameAddressRequest : IHomeGameAddressInput
{
    public string? PostalCode { get; set; }
    public string? Street { get; set; }
    public string? Number { get; set; }
    public string? Complement { get; set; }
    public string? District { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? AddressVisibility { get; set; }
}
