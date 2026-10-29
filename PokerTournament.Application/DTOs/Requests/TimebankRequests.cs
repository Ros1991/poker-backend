namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// "Usar timebank" (SPEC-20261029-1722). <see cref="ActionId"/>: id gerado pelo celular do dealer para este uso — o
/// "Tentar de novo" depois de uma falha de rede manda o mesmo id, e o servidor não desconta duas vezes (vira o id da
/// atividade TimebankUsed). Sem o campo, cada pedido é um uso novo.
/// </summary>
public class TimebankUseRequest
{
    public Guid? ActionId { get; set; }
}

/// <summary>Dar (+1) ou tirar (−1) um timebank do jogador (SPEC-20261029-1722); outro valor → 400.</summary>
public class TimebankAdjustRequest
{
    public int Delta { get; set; }
}
