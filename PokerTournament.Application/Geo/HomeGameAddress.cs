using System.Globalization;
using System.Text;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Geo;

/// <summary>Campos de endereço que chegam da tela (o PUT do home game e o PUT …/address trazem os mesmos).</summary>
public interface IHomeGameAddressInput
{
    string? PostalCode { get; }
    string? Street { get; }
    string? Number { get; }
    string? Complement { get; }
    string? District { get; }
    string? City { get; }
    string? State { get; }
    /// <summary>"ApprovedOnly" | "Public"; null/vazio = não muda (ou o padrão, no home game novo).</summary>
    string? AddressVisibility { get; }
}

/// <summary>O que identifica o lugar (sem visibilidade): muda → as coordenadas são refeitas.</summary>
public readonly record struct AddressSnapshot(string? PostalCode, string? Street, string? Number, string? District, string? City, string? State);

/// <summary>Um texto para o geocodificador e o nível de precisão que ele representa.</summary>
public sealed record GeocodeCandidate(GeocodePrecision Precision, string Query);

/// <summary>
/// Regras do endereço do home game (SPEC-20261108-1634): normalização e validação do que chega da tela, detecção de
/// mudança (zera as coordenadas), os textos de consulta por nível de precisão (decisão 3) e a chave normalizada do
/// cache. Tudo estático: quem chama (controller) grava e enfileira.
/// </summary>
public static class HomeGameAddress
{
    public const string InvalidCepMessage = "CEP inválido. Digite os 8 números do CEP.";
    public const string InvalidStateMessage = "UF inválida. Use a sigla com 2 letras (ex.: SP).";
    public const string InvalidVisibilityMessage = "Visibilidade do endereço inválida.";

    private static readonly HashSet<string> States = new(StringComparer.Ordinal)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI",
        "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO",
    };

    public static AddressSnapshot Snapshot(HomeGame homeGame) =>
        new(homeGame.PostalCode, homeGame.Street, homeGame.Number, homeGame.District, homeGame.City, homeGame.State);

    /// <summary>
    /// Copia o endereço do pedido para a entidade, normalizado (CEP só dígitos, UF maiúscula, textos com Trim e
    /// vazio → null; tamanhos cortados) e validado (<see cref="DomainException"/> → 400). Devolve se o LUGAR mudou
    /// (CEP, rua, número, bairro, cidade ou UF); nesse caso as coordenadas foram zeradas para a geocodificação refazer.
    /// A visibilidade muda sem mexer nas coordenadas.
    /// </summary>
    public static bool Apply(HomeGame homeGame, IHomeGameAddressInput input)
    {
        var before = Snapshot(homeGame);

        string? cep = null;
        if (!string.IsNullOrWhiteSpace(input.PostalCode))
            cep = ViaCepClient.NormalizeCep(input.PostalCode) ?? throw new DomainException(InvalidCepMessage);

        string? state = null;
        if (!string.IsNullOrWhiteSpace(input.State))
        {
            state = input.State.Trim().ToUpperInvariant();
            if (!States.Contains(state))
                throw new DomainException(InvalidStateMessage);
        }

        homeGame.PostalCode = cep;
        homeGame.Street = Clean(input.Street, 200);
        homeGame.Number = Clean(input.Number, 20);
        homeGame.Complement = Clean(input.Complement, 100);
        homeGame.District = Clean(input.District, 100);
        homeGame.City = Clean(input.City, 100);
        homeGame.State = state;

        if (!string.IsNullOrWhiteSpace(input.AddressVisibility))
            homeGame.AddressVisibility = ParseVisibility(input.AddressVisibility);
        else if (string.IsNullOrEmpty(homeGame.AddressVisibility))
            homeGame.AddressVisibility = nameof(AddressVisibility.ApprovedOnly);

        var changed = before != Snapshot(homeGame);
        if (changed)
        {
            homeGame.Latitude = null;
            homeGame.Longitude = null;
            homeGame.GeocodedAt = null;
            homeGame.GeocodePrecision = null;
            homeGame.GeocodeFailedAt = null;
        }
        return changed;
    }

    public static string ParseVisibility(string value)
    {
        var v = value.Trim();
        if (string.Equals(v, nameof(AddressVisibility.ApprovedOnly), StringComparison.OrdinalIgnoreCase))
            return nameof(AddressVisibility.ApprovedOnly);
        if (string.Equals(v, nameof(AddressVisibility.Public), StringComparison.OrdinalIgnoreCase))
            return nameof(AddressVisibility.Public);
        throw new DomainException(InvalidVisibilityMessage);
    }

    /// <summary>Tem endereço (o aviso do Início e o <c>hasAddress</c> da resposta): CEP ou cidade preenchidos.</summary>
    public static bool HasAddress(HomeGame homeGame) =>
        !string.IsNullOrEmpty(homeGame.PostalCode) || !string.IsNullOrEmpty(homeGame.City);

    /// <summary>Dá para geocodificar: existe pelo menos um candidato (rua + cidade + UF, ou CEP).</summary>
    public static bool IsGeocodable(HomeGame homeGame) => Candidates(homeGame).Count > 0;

    /// <summary>
    /// Textos de consulta do mais preciso ao menos preciso (decisão 3): "rua, número, cidade, UF" → "rua, cidade, UF"
    /// → "CEP, Brasil". Só cidade/UF (sem rua nem CEP) não gera candidato: não há nível para isso.
    /// </summary>
    public static IReadOnlyList<GeocodeCandidate> Candidates(HomeGame homeGame)
    {
        var list = new List<GeocodeCandidate>();
        var street = homeGame.Street;
        var city = homeGame.City;
        var state = homeGame.State;
        if (!string.IsNullOrEmpty(street) && !string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(state))
        {
            if (!string.IsNullOrEmpty(homeGame.Number) && HasDigit(homeGame.Number))
                list.Add(new GeocodeCandidate(GeocodePrecision.Address, $"{street}, {homeGame.Number}, {city}, {state}"));
            list.Add(new GeocodeCandidate(GeocodePrecision.Street, $"{street}, {city}, {state}"));
        }
        if (!string.IsNullOrEmpty(homeGame.PostalCode))
            list.Add(new GeocodeCandidate(GeocodePrecision.PostalCode, $"{ViaCepClient.FormatCep(homeGame.PostalCode)}, Brasil"));
        return list;
    }

    /// <summary>Chave do cache: minúsculas, sem acentos, espaços simples, até 500 caracteres.</summary>
    public static string CacheKey(string query)
    {
        var normalized = query.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastSpace = true;
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace)
                    sb.Append(' ');
                lastSpace = true;
                continue;
            }
            sb.Append(char.ToLowerInvariant(ch));
            lastSpace = false;
        }
        var key = sb.ToString().Trim();
        return key.Length > 500 ? key[..500] : key;
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var v = value.Trim();
        return v.Length > max ? v[..max] : v;
    }

    private static bool HasDigit(string value) => value.Any(char.IsAsciiDigit);
}
