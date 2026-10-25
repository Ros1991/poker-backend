namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// Onde a inscrição sentou cada um (SPEC-20261023-1214, MA16): mesas abertas, lugares dos novos e quem mudou de
/// lugar para equilibrar. Com <see cref="TablesEnabled"/> falso (plano do dono sem Tables) nada de mesa acontece e
/// as listas vêm vazias.
/// </summary>
public class SeatingResultResponse
{
    /// <summary>Plano do dono tem o recurso Tables: a inscrição senta (MA13)</summary>
    public bool TablesEnabled { get; set; }
    /// <summary>Torneio já começou (late registration): o aviso pede para mudar entre uma mão e outra (MA7)</summary>
    public bool InProgress { get; set; }
    public List<SeatingOpenedTable> OpenedTables { get; set; } = [];
    /// <summary>Lugar de cada inscrito agora (e, na primeira mesa, de quem já estava inscrito sem lugar — MA2)</summary>
    public List<SeatingSeat> Seated { get; set; } = [];
    /// <summary>Quem já estava sentado e mudou para uma mesa aberta agora (MA5)</summary>
    public List<SeatingMove> Moves { get; set; } = [];
    /// <summary>Texto pronto, ex.: "Mesa 2 aberta: 4 jogadores movidos"; nulo sem mesas</summary>
    public string? Summary { get; set; }
}

public class SeatingOpenedTable
{
    public Guid TableId { get; set; }
    public string TableName { get; set; } = string.Empty;
}

public class SeatingSeat
{
    public Guid EntryId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public Guid TableId { get; set; }
    public string TableName { get; set; } = string.Empty;
    public int SeatNumber { get; set; }
}

public class SeatingMove
{
    public Guid EntryId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public Guid FromTableId { get; set; }
    public string FromTableName { get; set; } = string.Empty;
    public int? FromSeat { get; set; }
    public Guid ToTableId { get; set; }
    public string ToTableName { get; set; } = string.Empty;
    public int ToSeat { get; set; }
}

/// <summary>Resposta de POST entries/bulk (MA16): as inscrições criadas (já com mesa/lugar) e o resultado do assento.</summary>
public class BulkCreateEntriesResponse
{
    public List<EntryResponse> Entries { get; set; } = [];
    public SeatingResultResponse Seating { get; set; } = new();
}
