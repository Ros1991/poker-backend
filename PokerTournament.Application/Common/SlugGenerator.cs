using System.Globalization;
using System.Text;

namespace PokerTournament.Application.Common;

/// <summary>
/// Gera o trecho legível de URL a partir de um nome:
/// "2º semestre 2026" → "2-semestre-2026", "Campeonato São João" → "campeonato-sao-joao".
/// </summary>
public static class SlugGenerator
{
    public const int MaxLength = 60;
    private const string Fallback = "ranking";

    public static string FromName(string? name)
    {
        // Ordinais (º ª) e o grau (°, digitado no lugar do º) somem: "1º" vira "1"
        var lowered = (name ?? string.Empty)
            .ToLowerInvariant()
            .Replace("º", string.Empty)
            .Replace("ª", string.Empty)
            .Replace("°", string.Empty);

        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingDash = false;

        foreach (var ch in decomposed)
        {
            // Acentos viram marcas separadas no FormD: descartá-las tira o acento
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');
                pendingDash = false;
                builder.Append(ch);
            }
            else
            {
                pendingDash = true;
            }
        }

        var slug = builder.ToString();
        if (slug.Length > MaxLength)
            slug = slug[..MaxLength].TrimEnd('-');

        return slug.Length == 0 ? Fallback : slug;
    }

    /// <summary>Devolve o slug base ou, se já usado, base-2, base-3…</summary>
    public static string MakeUnique(string baseSlug, IReadOnlySet<string> taken)
    {
        if (!taken.Contains(baseSlug))
            return baseSlug;

        for (var i = 2; ; i++)
        {
            var suffix = $"-{i}";
            var stem = baseSlug.Length + suffix.Length > MaxLength
                ? baseSlug[..(MaxLength - suffix.Length)].TrimEnd('-')
                : baseSlug;
            var candidate = stem + suffix;
            if (!taken.Contains(candidate))
                return candidate;
        }
    }
}
