namespace PokerTournament.Application.DTOs.Requests;

public class MovePlayerRequest
{
    public Guid EntryId { get; set; }
    public Guid ToTableId { get; set; }
    public int ToSeat { get; set; }
    // Opcional: "balance" quando a mudança veio do aviso de mesas desequilibradas (só muda o texto do histórico)
    public string? Reason { get; set; }
    /// <summary>
    /// Arrastar até um lugar VAZIO (SPEC-20261027-2133): se alguém sentou nele nesse meio-tempo (outro aparelho), recusa
    /// em vez de trocar com quem chegou — a troca é pedida de propósito pela rota <c>swap</c>. Sem o campo, o mover de
    /// sempre (lugar ocupado troca os dois).
    /// </summary>
    public bool RequireFreeSeat { get; set; }
}

/// <summary>
/// Troca de lugar entre dois jogadores sentados do mesmo torneio (SPEC-20261027-2133: soltar um jogador no lugar de
/// outro). Os lugares esperados são opcionais: quando vêm (o que o painel via ao soltar) e um dos dois mudou de lugar
/// nesse meio-tempo, a troca é recusada sem mexer em ninguém.
/// </summary>
public class SwapPlayersRequest
{
    /// <summary>Jogador arrastado</summary>
    public Guid EntryId { get; set; }
    /// <summary>Jogador do lugar onde ele foi solto</summary>
    public Guid OtherEntryId { get; set; }
    /// <summary>Onde o painel via o arrastado (opcional)</summary>
    public Guid? FromTableId { get; set; }
    public int? FromSeat { get; set; }
    /// <summary>Lugar onde ele foi solto, onde o painel via o outro (opcional)</summary>
    public Guid? ToTableId { get; set; }
    public int? ToSeat { get; set; }
}
