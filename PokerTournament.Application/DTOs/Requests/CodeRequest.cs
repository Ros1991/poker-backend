namespace PokerTournament.Application.DTOs.Requests;

/// <summary>Código lido de um QR (pareamento da TV ou acesso do dealer).</summary>
public class CodeRequest
{
    public string Code { get; set; } = string.Empty;
}
