namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// Adicionar jogador pelo terminal: PersonId (jogador já cadastrado no home game) OU
/// FullName/Nickname (cadastro rápido, só nome e apelido).
/// </summary>
public class QuickAddEntryRequest
{
    public Guid? PersonId { get; set; }
    public string? FullName { get; set; }
    public string? Nickname { get; set; }
    public bool BuyInPaid { get; set; }
}
