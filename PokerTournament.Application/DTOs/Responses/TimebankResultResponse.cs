namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// Resultado do uso ou do ajuste de timebank (SPEC-20261029-1722): a inscrição já com o novo
/// <see cref="EntryResponse.TimebanksRemaining"/> e a atividade gravada (o "Desfazer" do toast desfaz exatamente ela).
/// </summary>
public class TimebankResultResponse
{
    public EntryResponse Entry { get; set; } = null!;
    public Guid ActivityId { get; set; }
    /// <summary>Segundos de cada timebank do torneio (a contagem do celular usa este valor).</summary>
    public int Seconds { get; set; }
    /// <summary>true quando o mesmo ActionId já tinha sido registrado (o "Tentar de novo" não descontou outra vez).</summary>
    public bool AlreadyRegistered { get; set; }
}
