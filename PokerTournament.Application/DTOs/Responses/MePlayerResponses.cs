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
    /// <summary>
    /// Próximo torneio dos clubes onde a conta joga (SPEC-20261108-1633): o agendado/com inscrições abertas mais próximo,
    /// de hoje em diante, mesmo que a conta ainda não esteja inscrita. Null sem nenhum.
    /// </summary>
    public MyNextTournamentResponse? NextTournament { get; set; }
    /// <summary>Posição atual da conta em cada ranking de que participa (SPEC-20261108-1633); vazio sem ranking.</summary>
    public List<MyRankingPositionResponse> Rankings { get; set; } = [];
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

    // Ranking (SPEC-20261108-1633): só em torneio de ranking não excluído
    public Guid? RankingId { get; set; }
    public string? RankingName { get; set; }
    /// <summary>Pontos que a conta fez neste torneio no ranking (null: sem ranking, torneio não terminado ou sem posição).</summary>
    public decimal? RankingPoints { get; set; }
    /// <summary>Posição da conta no ranking logo DEPOIS deste torneio (classificação só com as etapas até ele).</summary>
    public int? RankingPositionAfter { get; set; }
    /// <summary>Quantos jogadores o ranking tinha depois deste torneio.</summary>
    public int? RankingPlayersAfter { get; set; }
    /// <summary>Quem eliminou a conta (nome público: apelido, ou primeiro nome + inicial), se registrado.</summary>
    public string? EliminatedByName { get; set; }
    /// <summary>Quantos jogadores a conta eliminou neste torneio (eliminações valendo).</summary>
    public int EliminationsCount { get; set; }
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

    // ---- SPEC-20261108-1633: terminal do jogador
    /// <summary>Hora do servidor na resposta (o celular conta o "você está no tempo" a partir dela).</summary>
    public DateTimeOffset ServerNow { get; set; }
    /// <summary>Timebanks por jogador no torneio (null = torneio sem timebank).</summary>
    public int? TimebankCount { get; set; }
    public int? TimebankSeconds { get; set; }
    /// <summary>Timebanks que a conta ainda tem (null = torneio sem timebank).</summary>
    public int? TimebanksRemaining { get; set; }
    /// <summary>
    /// "Você está no tempo": até quando vale o timebank que o dealer acabou de registrar para a conta — só quando o último
    /// uso de timebank do torneio (não desfeito, ainda dentro dos segundos) é de uma inscrição dela. Null fora disso.
    /// </summary>
    public DateTimeOffset? OnTheClockUntil { get; set; }
    /// <summary>A mesa da conta com os lugares (D3: apelido/foto dos demais, lugar vazio; nada de dinheiro). Só com Tables, sentado e jogando.</summary>
    public MyTableResponse? Table { get; set; }
    /// <summary>Bolha: quantos faltam para a premiação e quanto paga cada posição (só com Prizes e torneio acontecendo).</summary>
    public MyBubbleResponse? Bubble { get; set; }
    /// <summary>WhatsApp (só dígitos, com DDI) de quem responde pelo torneio — quem o criou, senão o dono do clube; null sem WhatsApp cadastrado.</summary>
    public string? OrganizerWhatsapp { get; set; }
    public string? OrganizerName { get; set; }
    /// <summary>Quem a conta eliminou (nome público), da mais recente para a mais antiga.</summary>
    public List<MyEliminationResponse> Eliminations { get; set; } = [];
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

/// <summary>A mesa da conta (SPEC-20261108-1633, D3): lugares de 1 a MaxSeats; nos ocupados, o nome público e a foto.</summary>
public class MyTableResponse
{
    public Guid TableId { get; set; }
    public int TableNumber { get; set; }
    public string? TableName { get; set; }
    public int MaxSeats { get; set; }
    public int? MySeat { get; set; }
    public List<MyTableSeatResponse> Seats { get; set; } = [];
}

public class MyTableSeatResponse
{
    public int SeatNumber { get; set; }
    public bool IsMe { get; set; }
    /// <summary>Null = lugar livre.</summary>
    public string? DisplayName { get; set; }
    public string? PhotoUrl { get; set; }
}

/// <summary>Bolha (SPEC-20261108-1633): os prêmios confirmados ou, sem confirmação, a sugestão que a aba Premiação mostra.</summary>
public class MyBubbleResponse
{
    public bool Confirmed { get; set; }
    public int PrizeCount { get; set; }
    public int PlayersRemaining { get; set; }
    /// <summary>Quantas eliminações faltam para todo mundo que sobrar estar na premiação (0 = já estão).</summary>
    public int PlacesToMoney { get; set; }
    public bool InTheMoney { get; set; }
    public List<MyPrizePositionResponse> Prizes { get; set; } = [];
}

public class MyPrizePositionResponse
{
    public int Position { get; set; }
    public decimal Amount { get; set; }
}

public class MyEliminationResponse
{
    public string DisplayName { get; set; } = string.Empty;
    public int? Position { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// GET me/tournaments/{id}/history (SPEC-20261108-1633): os lançamentos das inscrições da conta no torneio — inscrição,
/// rebuys, add-ons, pagamentos, prêmio pago, a eliminação dela e as que ela fez, mesa/lugar e timebank — do mais recente
/// para o mais antigo. Nada das inscrições dos outros.
/// </summary>
public class MyHistoryItemResponse
{
    /// <summary>BuyIn, Rebuy, Addon, Payment, Settlement, PrizePayment, Elimination, Knockout (a que a conta fez) ou o tipo da atividade.</summary>
    public string Kind { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public DateTimeOffset At { get; set; }
    /// <summary>Desfeito/estornado depois (continua na lista, riscado).</summary>
    public bool Undone { get; set; }
}

/// <summary>Próximo torneio dos clubes onde a conta joga (SPEC-20261108-1633).</summary>
public class MyNextTournamentResponse
{
    public Guid TournamentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    /// <summary>Draft ou OpenForRegistration.</summary>
    public string Status { get; set; } = string.Empty;
    public Guid HomeGameId { get; set; }
    public string HomeGameLabel { get; set; } = string.Empty;
    public decimal BuyInAmount { get; set; }
    /// <summary>Nome da estrutura de blinds (null = níveis próprios do torneio ou sem estrutura).</summary>
    public string? StructureName { get; set; }
    /// <summary>Bairro e cidade do clube ("Bela Vista, São Paulo"); null sem endereço. Nunca a rua.</summary>
    public string? LocationSummary { get; set; }
    /// <summary>A conta já está inscrita nele.</summary>
    public bool Registered { get; set; }
}

/// <summary>Posição da conta num ranking de que participa (números da temporada, SPEC-20261108-1633).</summary>
public class MyRankingPositionResponse
{
    public Guid RankingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Season { get; set; }
    public Guid HomeGameId { get; set; }
    public string HomeGameLabel { get; set; } = string.Empty;
    /// <summary>Null enquanto a conta não pontuou em nenhuma etapa terminada.</summary>
    public int? Position { get; set; }
    public int PlayerCount { get; set; }
    public decimal TotalPoints { get; set; }
    public int TotalStages { get; set; }
}

/// <summary>
/// GET me/rankings e me/rankings/{id} (SPEC-20261108-1633): o ranking como a página pública /r/{slug} o mostra (apelido,
/// foto, pontos e etapas de todo mundo — é público dentro do clube) mais a posição e os pontos da própria conta.
/// </summary>
public class MyRankingResponse : PublicRankingResponse
{
    public Guid RankingId { get; set; }
    public Guid HomeGameId { get; set; }
    public string HomeGameLabel { get; set; } = string.Empty;
    /// <summary>Os jogadores (cadastros) da conta neste clube — as linhas dela na classificação.</summary>
    public List<Guid> MyPersonIds { get; set; } = [];
    /// <summary>Melhor posição da conta na classificação (null sem etapa pontuada).</summary>
    public int? MyPosition { get; set; }
    public decimal MyTotalPoints { get; set; }
    public decimal MyDiscardedPoints { get; set; }
    public int MyTournamentsPlayed { get; set; }
}
