namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// GET api/v1/me/inicio (SPEC-20261029-1724): o Início da conta numa consulta só — plano, torneios acontecendo agora
/// nos home games que a conta acessa, os últimos torneios de cada home game e os clubes de outras pessoas em que ela é
/// membro. Evita uma consulta por home game na tela.
/// </summary>
public class InicioResponse
{
    /// <summary>Plano efetivo da conta.</summary>
    public string Plan { get; set; } = string.Empty;
    /// <summary>"Home game" aparece como conceito (seletor, criar, excluir)? Só na Liga (D18).</summary>
    public bool HomeGamesVisible { get; set; }
    /// <summary>Home games como dono, contando o principal (Mesa 1, Clube 1, Liga 3).</summary>
    public int MaxOwnedHomeGames { get; set; }
    /// <summary>Home game principal da conta (é nele que ficam os torneios no Mesa e no Clube).</summary>
    public Guid? PrimaryHomeGameId { get; set; }
    /// <summary>Torneios iniciados (inscrições abertas, em andamento ou rebuys finalizados), o mais recente primeiro.</summary>
    public List<InicioLiveTournament> Live { get; set; } = [];
    /// <summary>
    /// Home games que a conta acessa: os dela primeiro (o principal à frente, depois por nome) e depois os de outras
    /// pessoas em que ela é membro (por nome). Só os ativos, como o GET HomeGames.
    /// </summary>
    public List<InicioHomeGame> HomeGames { get; set; } = [];
}

public class InicioTournament
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public int TotalEntries { get; set; }
    public int PlayersRemaining { get; set; }
}

public class InicioLiveTournament : InicioTournament
{
    public Guid HomeGameId { get; set; }
    /// <summary>Nome do home game para a interface (null no principal de dono Mesa, como o displayName).</summary>
    public string? HomeGameName { get; set; }
    public int? CurrentLevel { get; set; }
    /// <summary>Relógio correndo (falso = pausado).</summary>
    public bool IsTimerRunning { get; set; }
    /// <summary>Quem está logado gerencia o home game (dono, membro administrador ou Admin do sistema).</summary>
    public bool CanManage { get; set; }
}

public class InicioHomeGame
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Null no principal de dono Mesa (no Mesa não existe nome de clube); senão o nome.</summary>
    public string? DisplayName { get; set; }
    public bool IsPrimary { get; set; }
    /// <summary>O principal ainda tem o nome padrão "Clube de &lt;nome&gt;" (dica "Dê um nome ao seu clube").</summary>
    public bool IsNameDefault { get; set; }
    public bool IsActive { get; set; }
    /// <summary>A conta logada é a dona (senão é membro).</summary>
    public bool IsOwner { get; set; }
    public bool CanManage { get; set; }
    public Guid OwnerId { get; set; }
    public string? OwnerName { get; set; }
    /// <summary>Plano efetivo do dono (as seções e os recursos do home game seguem ele).</summary>
    public string OwnerPlan { get; set; } = string.Empty;
    /// <summary>Torneios do home game, sem cancelados (a mesma contagem do GET HomeGames).</summary>
    public int TournamentCount { get; set; }
    /// <summary>Os 5 torneios mais recentes pela data (sem cancelados).</summary>
    public List<InicioTournament> Recent { get; set; } = [];
    /// <summary>Próximo torneio: o primeiro ainda não finalizado (rascunho ou iniciado) pela data.</summary>
    public InicioTournament? Next { get; set; }
    /// <summary>Último torneio finalizado.</summary>
    public InicioTournament? Last { get; set; }
}
