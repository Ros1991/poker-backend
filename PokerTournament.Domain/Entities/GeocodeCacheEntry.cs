namespace PokerTournament.Domain.Entities;

/// <summary>
/// Cache das consultas ao geocodificador (SPEC-20261108-1634): uma linha por texto de consulta normalizado
/// (<see cref="QueryKey"/>), com o resultado — achado (coordenadas) ou não achado. Sobrevive ao reinício da API e
/// evita repetir ao Nominatim a mesma pergunta (dois home games no mesmo endereço, o mesmo endereço salvo de novo).
/// </summary>
public class GeocodeCacheEntry : BaseEntity
{
    /// <summary>Texto da consulta normalizado (minúsculas, sem acentos, espaços simples).</summary>
    public string QueryKey { get; set; } = string.Empty;
    /// <summary>O geocodificador achou um ponto para a consulta.</summary>
    public bool Found { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>Quem respondeu (ex.: "Nominatim").</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Nome do lugar devolvido pelo provedor (para conferir à mão), até 500 caracteres.</summary>
    public string? ResolvedName { get; set; }
}
