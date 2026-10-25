namespace PokerTournament.Application.Common;

/// <summary>Nomes de jogador: espaços normalizados e chave de comparação (cadastro sem duplicado no home game).</summary>
public static class PlayerNames
{
    /// <summary>Tira espaços das pontas e junta os do meio ("  Zé   da  Silva " → "Zé da Silva").</summary>
    public static string NormalizeSpaces(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Chave que ignora maiúsculas e espaços: dois nomes com a mesma chave são o mesmo jogador.</summary>
    public static string NameKey(string? value) => NormalizeSpaces(value).ToUpperInvariant();
}
