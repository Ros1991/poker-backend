using System.Text;
using System.Text.RegularExpressions;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Pix;

/// <summary>Resultado do <see cref="PixKeyNormalizer.Normalize"/>.</summary>
/// <param name="Ok">false = chave recusada (<see cref="ErrorCode"/>/<see cref="Error"/>).</param>
/// <param name="Type">Tipo detectado ou escolhido; null com Ok e Key nulos = vazio (remover a chave).</param>
/// <param name="Key">Chave no formato do DICT (o que se grava).</param>
/// <param name="Display">Como a tela mostra ("+55 61 99999-9999", "123.456.789-09").</param>
/// <param name="Alternatives">Só no caso ambíguo (11 dígitos sem formatação que são CPF válido E celular): [Phone].</param>
public sealed record PixKeyResult(
    bool Ok,
    PixKeyType? Type,
    string? Key,
    string? Display,
    IReadOnlyList<PixKeyType> Alternatives,
    string? ErrorCode,
    string? Error)
{
    public static readonly PixKeyResult Empty = new(true, null, null, null, [], null, null);
}

/// <summary>
/// Normalizador único da chave PIX (SPEC-20261023-1218, PX1): aceita o que a pessoa digita ("61 99999-9999",
/// "123.456.789-09", "Fulano@Gmail.com ") e devolve a chave no formato do DICT do Banco Central ("+5561999999999",
/// "12345678909", "fulano@gmail.com"), ou a mensagem de erro exata.
///
/// GÊMEO do <c>normalizePixKey</c> do frontend (<c>poker-frontend/src/utils/pix.ts</c>), com as mesmas regras e as
/// mesmas mensagens. A fonte da verdade é a tabela <c>poker-frontend/scripts/pix-cases.json</c>: mudou a regra,
/// muda dos dois lados e na tabela (o script de verificação da SPEC roda a tabela contra este código e contra a API).
///
/// Não reaproveita o <see cref="Common.PhoneNumbers"/> (WhatsApp, SPEC-20261023-1213) de propósito: a chave de celular
/// é mais estrita (DDDs da Anatel, 9 obrigatório, fixo recusado). Não unificar.
///
/// Detalhes que mantêm os dois lados iguais: só [0-9] como dígito (o \d do .NET aceita outros alfabetos), espaços
/// numa lista fechada (o \s e o Trim do .NET e do JS diferem) e maiúsculas/minúsculas só em A–Z.
/// </summary>
public static class PixKeyNormalizer
{
    public static readonly IReadOnlyDictionary<string, string> Messages = new Dictionary<string, string>
    {
        ["cpf"] = "CPF inválido: confira os números.",
        ["cnpj"] = "CNPJ inválido: confira os números.",
        ["celular"] = "Celular inválido: use DDD + 9 dígitos (ex.: 61 99999-9999).",
        ["sem9"] = "Celular precisa de 9 dígitos depois do DDD — faltou o 9 na frente?",
        ["fixo"] = "Telefone fixo não pode ser chave PIX. Use um celular com DDD.",
        ["estrangeiro"] = "Só celular do Brasil (+55) pode ser chave PIX.",
        ["email"] = "E-mail inválido para chave PIX.",
        ["email77"] = "E-mail com mais de 77 caracteres não pode ser chave PIX.",
        ["evp"] = "Chave aleatória inválida: são 32 letras e números no formato 8-4-4-4-12.",
        ["formato"] = "Não reconhecemos o formato da chave PIX. Use CPF, CNPJ, celular com DDD, e-mail ou chave aleatória.",
        ["ambigua"] = "Esta chave pode ser CPF ou celular. Escolha o tipo.",
        ["tipo"] = "Tipo de chave PIX inválido.",
    };

    /// <summary>Maior chave aceita pelo DICT (e o que cabe no campo 26 do BR Code).</summary>
    public const int MaxKeyLength = 77;

    /// <summary>DDDs válidos (Anatel, 67).</summary>
    private static readonly HashSet<string> Ddds =
    [
        "11", "12", "13", "14", "15", "16", "17", "18", "19",
        "21", "22", "24", "27", "28",
        "31", "32", "33", "34", "35", "37", "38",
        "41", "42", "43", "44", "45", "46", "47", "48", "49",
        "51", "53", "54", "55",
        "61", "62", "63", "64", "65", "66", "67", "68", "69",
        "71", "73", "74", "75", "77", "79",
        "81", "82", "83", "84", "85", "86", "87", "88", "89",
        "91", "92", "93", "94", "95", "96", "97", "98", "99",
    ];

    private const string Spaces = " \t\n\r\f\v ";
    private const string Separators = Spaces + ".-/()";
    /// <summary>Separador que, logo depois dos 2 primeiros dígitos, indica celular ("61 9…", "61-9…").</summary>
    private const string PhoneSeparators = Spaces + ".-";

    private const RegexOptions Opts = RegexOptions.CultureInvariant;
    // Regex do DICT para a chave EMAIL, copiada literalmente da página da API DICT (campo Key).
    private static readonly Regex EmailRe = new(
        @"^[a-z0-9.!#$'*+\/=?^_`{|}~-]+@[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*$", Opts);
    private static readonly Regex UuidRe = new(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", Opts);
    private static readonly Regex Hex32Re = new("^[0-9a-fA-F]{32}$", Opts);
    private static readonly Regex HexDashRe = new("^[0-9a-fA-F-]+$", Opts);
    private static readonly Regex CpfMaskRe = new(@"^[0-9]{3}\.[0-9]{3}\.[0-9]{3}-[0-9]{2}$", Opts);
    private static readonly Regex CnpjMaskRe = new(
        @"^[0-9A-Za-z]{2}\.[0-9A-Za-z]{3}\.[0-9A-Za-z]{3}/[0-9A-Za-z]{4}-[0-9]{2}$", Opts);
    private static readonly Regex CnpjShapeRe = new("^[0-9A-Z]{12}[0-9]{2}$", Opts);
    private static readonly Regex AlnumRe = new("^[0-9A-Za-z]+$", Opts);
    private static readonly Regex DigitsRe = new("^[0-9]+$", Opts);
    private static readonly Regex LetterRe = new("[A-Za-z]", Opts);
    private static readonly Regex Hex32LowerRe = new("^[0-9a-f]{32}$", Opts);
    private static readonly Regex Cpf11Re = new("^[0-9]{11}$", Opts);

    /// <summary>
    /// Normaliza a chave digitada. Sem tipo: detecta (e-mail → chave aleatória → "+" celular → máscaras de CPF/CNPJ →
    /// letras = CNPJ alfanumérico → só dígitos pelo tamanho; 11 dígitos pela regra CPF × celular). Com tipo: valida só
    /// como aquele tipo (texto do tipo desconhecido → erro "tipo"). Vazio → <see cref="PixKeyResult.Empty"/>.
    /// </summary>
    public static PixKeyResult Normalize(string? input, string? type = null)
    {
        var s = TrimSpaces(input ?? string.Empty);
        if (s.Length == 0)
            return PixKeyResult.Empty;

        if (!TryParseType(type, out var chosen))
            return Fail("tipo");
        if (chosen is { } t)
            return ValidateAs(t, s);

        if (s.Contains('@'))
            return ValidateEmail(s);

        var inner = StripBraces(s);
        if (UuidRe.IsMatch(inner) || Hex32Re.IsMatch(inner) || (HexDashRe.IsMatch(inner) && inner.Length >= 30))
            return ValidateEvp(s);

        if (s.StartsWith('+'))
            return ValidatePhone(s);
        if (CpfMaskRe.IsMatch(s))
            return ValidateCpf(s);
        if (CnpjMaskRe.IsMatch(s))
            return ValidateCnpj(s);

        var stripped = StripSeparators(s);
        if (!AlnumRe.IsMatch(stripped))
            return Fail("formato");
        if (LetterRe.IsMatch(stripped))
            return CnpjShapeRe.IsMatch(AsciiUpper(stripped)) ? ValidateCnpj(s) : Fail("formato");

        switch (stripped.Length)
        {
            case 11:
            {
                // A formatação desempata: parênteses ou separador logo depois dos 2 primeiros dígitos = celular
                var phoneFormatted = s.Contains('(') || s.Contains(')')
                    || (s.Length > 2 && IsAsciiDigit(s[0]) && IsAsciiDigit(s[1]) && PhoneSeparators.Contains(s[2]));
                if (phoneFormatted)
                    return ValidatePhone(s);
                var cpfOk = IsValidCpf(stripped);
                var phoneOk = Ddds.Contains(stripped[..2]) && stripped[2] == '9';
                if (cpfOk && phoneOk)
                {
                    var cpf = ValidateCpf(s);
                    return cpf with { Alternatives = [PixKeyType.Phone] };
                }
                if (cpfOk)
                    return ValidateCpf(s);
                if (phoneOk)
                    return ValidatePhone(s);
                return Fail(stripped[2] == '9' ? "celular" : "cpf");
            }
            case 13:
                return stripped.StartsWith("55", StringComparison.Ordinal) ? ValidatePhone(s) : Fail("formato");
            case 15:
                return stripped.StartsWith("0055", StringComparison.Ordinal) ? ValidatePhone(s) : Fail("formato");
            case 12:
                return stripped.StartsWith('0') || stripped.StartsWith("55", StringComparison.Ordinal)
                    ? ValidatePhone(s)
                    : Fail("formato");
            case 10:
                return ValidatePhone(s);
            case 14:
                return ValidateCnpj(s);
            default:
                return Fail("formato");
        }
    }

    /// <summary>
    /// Chave para gravar: a normalizada, ou null quando vazia (remove a chave). Lança <see cref="DomainException"/>
    /// (400) com a mensagem da tabela — inclusive "Esta chave pode ser CPF ou celular. Escolha o tipo." quando o
    /// número é as duas coisas e o tipo não veio, e "Tipo de chave PIX inválido." para um tipo que não existe.
    /// </summary>
    public static string? NormalizeForWrite(string? input, string? type)
    {
        var result = Normalize(input, type);
        if (!result.Ok)
            throw new DomainException(result.Error!);
        if (result.Key is null)
            return null;
        if (result.Alternatives.Count > 0 && string.IsNullOrWhiteSpace(type))
            throw new DomainException(Messages["ambigua"]);
        return result.Key;
    }

    /// <summary>
    /// Edição de um registro que já tem chave: igual ao <see cref="NormalizeForWrite"/>, mas o MESMO texto já gravado
    /// que não normaliza (chave antiga fora de formato, que o boot deixou intacta, ou o caso ambíguo reenviado sem
    /// tipo) continua como está em vez de dar 400 — senão um campo escondido (plano Mesa) ou a edição de outro campo
    /// travaria por causa de uma chave que ninguém mexeu. Texto novo é sempre validado.
    /// </summary>
    public static string? NormalizeForUpdate(string? input, string? type, string? current)
    {
        try
        {
            return NormalizeForWrite(input, type);
        }
        catch (DomainException) when (current is not null && string.Equals(TrimSpaces(input ?? string.Empty), current, StringComparison.Ordinal))
        {
            return current;
        }
    }

    /// <summary>Tipo (nome do <see cref="PixKeyType"/>) de uma chave gravada; null se vazia ou fora de formato.</summary>
    public static string? TypeOf(string? normalizedKey)
    {
        var result = Normalize(normalizedKey);
        return result is { Ok: true, Type: { } type } ? type.ToString() : null;
    }

    /// <summary>Texto do tipo (tela/API) → tipo, sem diferenciar maiúsculas; vazio = automático (true com null).</summary>
    public static bool TryParseType(string? type, out PixKeyType? parsed)
    {
        parsed = null;
        if (type is null)
            return true;
        var t = TrimSpaces(type);
        if (t.Length == 0)
            return true;
        foreach (var value in Enum.GetValues<PixKeyType>())
        {
            if (string.Equals(AsciiLower(value.ToString()), AsciiLower(t), StringComparison.Ordinal))
            {
                parsed = value;
                return true;
            }
        }
        return false;
    }

    /// <summary>CPF: 11 dígitos, não todos iguais, os dois dígitos verificadores certos.</summary>
    public static bool IsValidCpf(string digits)
    {
        if (!Cpf11Re.IsMatch(digits) || AllSame(digits))
            return false;
        foreach (var len in new[] { 9, 10 })
        {
            var sum = 0;
            for (var i = 0; i < len; i++)
                sum += (digits[i] - '0') * (len + 1 - i);
            var dv = sum * 10 % 11 % 10;
            if (dv != digits[len] - '0')
                return false;
        }
        return true;
    }

    /// <summary>CNPJ (também o alfanumérico): valor de cada caractere = código ASCII − 48, pesos 2–9 da direita.</summary>
    public static bool IsValidCnpj(string key)
    {
        if (!CnpjShapeRe.IsMatch(key) || AllSame(key))
            return false;
        foreach (var len in new[] { 12, 13 })
        {
            var sum = 0;
            var weight = 2;
            for (var i = len - 1; i >= 0; i--)
            {
                sum += (key[i] - 48) * weight;
                weight = weight == 9 ? 2 : weight + 1;
            }
            var r = sum % 11;
            var dv = r < 2 ? 0 : 11 - r;
            if (dv != key[len] - 48)
                return false;
        }
        return true;
    }

    private static PixKeyResult ValidateAs(PixKeyType type, string s) => type switch
    {
        PixKeyType.Cpf => ValidateCpf(s),
        PixKeyType.Cnpj => ValidateCnpj(s),
        PixKeyType.Phone => ValidatePhone(s),
        PixKeyType.Email => ValidateEmail(s),
        _ => ValidateEvp(s),
    };

    private static PixKeyResult ValidateCpf(string s)
    {
        var t = StripSeparators(s);
        if (!IsValidCpf(t))
            return Fail("cpf");
        return Success(PixKeyType.Cpf, t, $"{t[..3]}.{t[3..6]}.{t[6..9]}-{t[9..]}");
    }

    private static PixKeyResult ValidateCnpj(string s)
    {
        var t = AsciiUpper(StripSeparators(s));
        if (!IsValidCnpj(t))
            return Fail("cnpj");
        return Success(PixKeyType.Cnpj, t, $"{t[..2]}.{t[2..5]}.{t[5..8]}/{t[8..12]}-{t[12..]}");
    }

    private static PixKeyResult ValidatePhone(string s)
    {
        var hasPlus = s.StartsWith('+');
        var digits = StripSeparators(hasPlus ? s[1..] : s);
        if (!DigitsRe.IsMatch(digits))
            return Fail("celular");

        string national;
        if (hasPlus)
        {
            if (!digits.StartsWith("55", StringComparison.Ordinal))
                return Fail("estrangeiro");
            national = digits[2..];
        }
        else if (digits.StartsWith("0055", StringComparison.Ordinal))
            national = digits[4..];
        else if (digits.StartsWith("55", StringComparison.Ordinal) && digits.Length is 12 or 13)
            national = digits[2..];
        else
            national = digits;

        // 0 de discagem na frente do DDD (nenhum DDD começa com 0)
        if (national.StartsWith('0') && national.Length is 11 or 12)
            national = national[1..];

        if (national.Length == 11)
        {
            if (!Ddds.Contains(national[..2]) || national[2] != '9')
                return Fail("celular");
        }
        else if (national.Length == 10)
        {
            if (!Ddds.Contains(national[..2]))
                return Fail("celular");
            if ("6789".Contains(national[2]))
                return Fail("sem9");
            if ("2345".Contains(national[2]))
                return Fail("fixo");
            return Fail("celular");
        }
        else
        {
            return Fail("celular");
        }
        return Success(PixKeyType.Phone, "+55" + national, $"+55 {national[..2]} {national[2..7]}-{national[7..]}");
    }

    private static PixKeyResult ValidateEmail(string s)
    {
        if (s.Length > MaxKeyLength)
            return Fail("email77");
        var key = AsciiLower(s);
        if (!EmailRe.IsMatch(key))
            return Fail("email");
        return Success(PixKeyType.Email, key, key);
    }

    private static PixKeyResult ValidateEvp(string s)
    {
        var hex = AsciiLower(StripBraces(s).Replace("-", string.Empty, StringComparison.Ordinal));
        if (!Hex32LowerRe.IsMatch(hex))
            return Fail("evp");
        var key = $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
        return Success(PixKeyType.Evp, key, key);
    }

    private static PixKeyResult Fail(string code) => new(false, null, null, null, [], code, Messages[code]);

    private static PixKeyResult Success(PixKeyType type, string key, string display) =>
        new(true, type, key, display, [], null, null);

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static string TrimSpaces(string s)
    {
        var a = 0;
        var b = s.Length;
        while (a < b && Spaces.Contains(s[a]))
            a++;
        while (b > a && Spaces.Contains(s[b - 1]))
            b--;
        return s[a..b];
    }

    private static string StripSeparators(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (!Separators.Contains(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static string StripBraces(string s) =>
        s.Length >= 2 && s[0] == '{' && s[^1] == '}' ? s[1..^1] : s;

    private static bool AllSame(string s)
    {
        for (var i = 1; i < s.Length; i++)
        {
            if (s[i] != s[0])
                return false;
        }
        return true;
    }

    // Só A–Z/a–z: acento e outros alfabetos ficam como estão (e as regex os recusam), igual nos dois lados.
    private static string AsciiLower(string s) =>
        string.Create(s.Length, s, (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
                span[i] = src[i] is >= 'A' and <= 'Z' ? (char)(src[i] + 32) : src[i];
        });

    private static string AsciiUpper(string s) =>
        string.Create(s.Length, s, (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
                span[i] = src[i] is >= 'a' and <= 'z' ? (char)(src[i] - 32) : src[i];
        });
}
