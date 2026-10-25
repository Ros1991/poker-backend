namespace PokerTournament.Application.Common;

/// <summary>
/// WhatsApp/celular brasileiro no servidor (SPEC-20261023-1213, F18) — mesma regra do front (<c>utils/phone.ts</c>):
/// guarda só os dígitos locais (DDD + número, 10 ou 11 dígitos), sem o 55 do país. <see cref="ToE164Br"/> dá o
/// formato internacional (55 + DDD + número) para quem precisa dele (WhatsApp da conta).
/// A chave PIX de celular NÃO passa por aqui: ela usa o <see cref="Pix.PixKeyNormalizer"/> (SPEC-20261023-1218), que é
/// mais estrito (DDDs da Anatel, 9 obrigatório, fixo recusado, gravado como +55…). Não unificar as duas regras.
/// </summary>
public static class PhoneNumbers
{
    public const string InvalidWhatsappMessage = "Informe o WhatsApp com DDD (10 ou 11 dígitos).";

    /// <summary>
    /// Só dígitos; tira os zeros do começo (o 0 do DDD/operadora e o 00 internacional — nenhum DDD começa com 0) e o
    /// 55 inicial quando sobram mais de 11 dígitos (e de novo os zeros que vierem depois dele). Vazio → null.
    /// Ex.: "+55 (11) 99999-9999", "011 99999-9999", "0055 11 99999-9999" e "+55 0 11 99999-9999" → "11999999999".
    /// Não corta nem recusa: quem exige 10–11 dígitos confere com <see cref="IsValidLocal"/> (o cadastro pelo torneio
    /// confere; o /Persons só normaliza). Mesma regra do <c>cleanWhatsapp</c> do front.
    /// </summary>
    public static string? NormalizeLocal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
        if (digits.Length > 11 && digits.StartsWith("55", StringComparison.Ordinal))
            digits = digits[2..].TrimStart('0');
        return digits.Length == 0 ? null : digits;
    }

    /// <summary>Número local válido: 10 (fixo/antigo) ou 11 dígitos (celular).</summary>
    public static bool IsValidLocal(string? local) =>
        local is { Length: 10 or 11 } && local.All(char.IsAsciiDigit);

    /// <summary>55 + DDD + número (sem "+"); null quando não dá um número local válido.</summary>
    public static string? ToE164Br(string? value)
    {
        var local = NormalizeLocal(value);
        return IsValidLocal(local) ? "55" + local : null;
    }
}
