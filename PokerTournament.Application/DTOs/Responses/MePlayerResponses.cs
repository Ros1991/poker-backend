namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// Área do jogador (SPEC-20261029-1726, D13/D14): o que a conta logada jogou e está jogando, em qualquer home game, lido
/// SÓ das inscrições cujo jogador está vinculado à conta (<c>Person.UserId</c>, SPEC-20261029-1725). Nada de outros
/// jogadores além das contagens do torneio (jogando X de Y).
/// </summary>
public class MePlayerSummaryResponse
{
    /// <summary>E-mail da conta (a dica "peça ao organizador para cadastrar seu e-mail" usa).</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>Torneios em que a conta tem inscrição (sem cancelados nem excluídos).</summary>
    public int TournamentCount { get; set; }
    /// <summary>Torneios que a conta venceu (posição final 1).</summary>
    public int Wins { get; set; }
    /// <summary>Torneios em que a conta ficou na premiação (ITM).</summary>
    public int InTheMoney { get; set; }
    /// <summary>
    /// Quanto a conta ainda deve, somando os torneios cujo organizador tem o controle de pagamentos no plano (sem ele o
    /// organizador não registra pagamento e o "deve" não quer dizer nada).
    /// </summary>
    public decimal OpenBalance { get; set; }
    /// <summary>Em quantos torneios há saldo em aberto.</summary>
    public int OpenBalanceCount { get; set; }
    /// <summary>Onde a conta joga (para o filtro de /meus-torneios), o do torneio mais recente primeiro.</summary>
    public List<MePlayerHomeGame> HomeGames { get; set; } = [];
    /// <summary>Os 5 torneios mais recentes.</summary>
    public List<MyTournamentResponse> Recent { get; set; } = [];
    /// <summary>Torneios iniciados (inscrições abertas, em andamento ou rebuys finalizados) em que a conta está inscrita.</summary>
    public List<MyTournamentResponse> Live { get; set; } = [];
}

public class MePlayerHomeGame
{
    public Guid Id { get; set; }
    /// <summary>
    /// Rótulo de "onde joguei": o nome do clube (Clube/Liga e home games extras); no principal de dono Mesa (sem nome de
    /// clube) "Torneios de &lt;nome do organizador&gt;" — ou "Seus torneios" quando o organizador é a própria conta.
    /// </summary>
    public string Label { get; set; } = string.Empty;
    public int TournamentCount { get; set; }
}

/// <summary>
/// Um torneio da conta (linha de /meus-torneios e do Início). Com reentrada, as inscrições da conta no mesmo torneio
/// viram uma linha só: contagens e valores somados, status/posição/mesa da inscrição mais recente. Com uma inscrição, os
/// números são os mesmos da aba Pagamentos/Jogadores do organizador.
/// </summary>
public class MyTournamentResponse
{
    public Guid TournamentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    /// <summary>Status do torneio (Draft, OpenForRegistration, InProgress, BreakSettlement, Finished).</summary>
    public string Status { get; set; } = string.Empty;
    public Guid HomeGameId { get; set; }
    /// <summary>Rótulo do "onde joguei" (ver <see cref="MePlayerHomeGame.Label"/>).</summary>
    public string HomeGameLabel { get; set; } = string.Empty;
    /// <summary>None, Simple ou Progressive.</summary>
    public string KnockoutType { get; set; } = string.Empty;
    /// <summary>Entradas do torneio e quantos ainda jogam (só as contagens).</summary>
    public int TotalEntries { get; set; }
    public int PlayersRemaining { get; set; }
    public int? CurrentLevel { get; set; }
    public bool IsTimerRunning { get; set; }

    /// <summary>O organizador tem o controle de pagamentos (Payments): sem ele, devido/pago/saldo vêm null.</summary>
    public bool ShowsPayments { get; set; }
    /// <summary>O organizador tem a premiação (Prizes): sem ela, o prêmio vem null.</summary>
    public bool ShowsPrizes { get; set; }

    /// <summary>Inscrições da conta neste torneio (mais de 1 = reentrada).</summary>
    public int EntryCount { get; set; }
    /// <summary>Status da inscrição mais recente (Registered, Active, Eliminated, Awarded, PaidOut).</summary>
    public string EntryStatus { get; set; } = string.Empty;
    /// <summary>Posição final da inscrição mais recente (null enquanto joga).</summary>
    public int? FinalPosition { get; set; }
    public int RebuyCount { get; set; }
    public int AddonCount { get; set; }
    public decimal? TotalDue { get; set; }
    public decimal? TotalPaid { get; set; }
    /// <summary>Devido − pago (positivo = deve; negativo = pagou a mais).</summary>
    public decimal? Balance { get; set; }
    /// <summary>Paid, PartiallyPaid, Pending ou Overpaid (a mesma regra da inscrição).</summary>
    public string? PaymentStatus { get; set; }
    public decimal? PrizeAmount { get; set; }
    /// <summary>Bounties ganhos (só no torneio com bounty; null no Normal).</summary>
    public decimal? BountyWon { get; set; }
}

/// <summary>
/// GET me/tournaments/{id}: o torneio ao vivo para quem joga — relógio, níveis (para o "próximo intervalo"), jogando X de
/// Y, premiação total (com Prizes) e a ficha da conta. 404 quando a conta não tem inscrição no torneio.
/// </summary>
public class MyTournamentLiveResponse : MyTournamentResponse
{
    /// <summary>Estado do relógio (null quando o torneio não tem estrutura de blinds).</summary>
    public TimerStateResponse? Timer { get; set; }
    /// <summary>Níveis do torneio em ordem (os próprios; sem eles, os da estrutura).</summary>
    public List<MyBlindLevel> BlindLevels { get; set; } = [];
    /// <summary>Premiação total (NetPrizePool), só com Prizes.</summary>
    public decimal? PrizePool { get; set; }
    /// <summary>Chave PIX do torneio (atalho para pagar o organizador), só com Payments.</summary>
    public string? PixKey { get; set; }
    /// <summary>O organizador tem o controle de mesas (Tables): sem ele, mesa e lugar vêm null.</summary>
    public bool ShowsTables { get; set; }
    public int? TableNumber { get; set; }
    public string? TableName { get; set; }
    public int? SeatNumber { get; set; }
    /// <summary>Bounty na cabeça da conta agora (só no torneio com bounty).</summary>
    public decimal? BountyHead { get; set; }
    public DateTimeOffset? EliminatedAt { get; set; }
}

public class MyBlindLevel
{
    public int LevelNumber { get; set; }
    public int SmallBlind { get; set; }
    public int BigBlind { get; set; }
    public int Ante { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsBreak { get; set; }
}
