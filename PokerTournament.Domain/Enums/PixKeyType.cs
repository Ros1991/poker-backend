namespace PokerTournament.Domain.Enums;

/// <summary>
/// Tipo da chave PIX com os nomes do DICT do Banco Central (SPEC-20261023-1218, PX2). Gravado como string via
/// <c>nameof</c> (<c>cost_extras.pix_key_type</c>, <c>blind_structure_default_costs.pix_key_type</c>); nas outras
/// tabelas o tipo sai da própria chave normalizada (os formatos são disjuntos). Na tela: "CPF", "CNPJ", "Celular",
/// "E-mail", "Chave aleatória".
/// </summary>
public enum PixKeyType
{
    /// <summary>11 dígitos.</summary>
    Cpf,
    /// <summary>14 caracteres maiúsculos (12 [0-9A-Z] + 2 dígitos — inclui o CNPJ alfanumérico).</summary>
    Cnpj,
    /// <summary>Celular do Brasil: +55 + DDD + 9 dígitos.</summary>
    Phone,
    /// <summary>E-mail minúsculo, até 77 caracteres.</summary>
    Email,
    /// <summary>Chave aleatória: UUID minúsculo 8-4-4-4-12.</summary>
    Evp,
}
