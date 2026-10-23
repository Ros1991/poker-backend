namespace PokerTournament.Application.DTOs.Requests;

/// <summary>Pedido de acesso do dealer: código do QR, nome informado e chave do aparelho.</summary>
public class DealerRequestCreateRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DeviceKey { get; set; } = string.Empty;
}
