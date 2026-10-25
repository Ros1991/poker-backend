namespace PokerTournament.Application.DTOs.Requests;

public class UpdatePixKeyRequest
{
    public string? PixKey { get; set; }

    /// <summary>
    /// Tipo escolhido na tela (Cpf, Cnpj, Phone, Email, Evp); null/vazio = detectar. Obrigatório só quando os 11 dígitos
    /// podem ser CPF ou celular (senão 400 "Esta chave pode ser CPF ou celular. Escolha o tipo.") — SPEC-20261023-1218.
    /// </summary>
    public string? PixKeyType { get; set; }
}
