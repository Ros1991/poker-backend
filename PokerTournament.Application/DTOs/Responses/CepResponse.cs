namespace PokerTournament.Application.DTOs.Responses;

/// <summary>GET cep/{cep} (SPEC-20261108-1634): o endereço do ViaCEP para preencher o formulário.</summary>
public class CepResponse
{
    /// <summary>Só os 8 dígitos.</summary>
    public string Cep { get; set; } = string.Empty;
    public string? Street { get; set; }
    /// <summary>Complemento que o ViaCEP manda (ex.: "de 1000 a 2000 - lado par"); a tela mostra como dica, não preenche.</summary>
    public string? Complement { get; set; }
    /// <summary>Bairro.</summary>
    public string? District { get; set; }
    public string? City { get; set; }
    /// <summary>UF com 2 letras.</summary>
    public string? State { get; set; }
}
