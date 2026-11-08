namespace PokerTournament.Application.DTOs.Responses;

// Perfil público do jogador e "jogou com você" (SPEC-20261108-1636). REGRA: nada de nome completo, e-mail, WhatsApp,
// documento ou dinheiro (prêmio, buy-in, saldo) de terceiros — nem como campo nulo. Quem acrescentar campo aqui confere
// a suíte api-1636.js (ela lista as chaves do JSON).

/// <summary>
/// <c>GET players/{handle}</c>. Com <see cref="IsPrivate"/> verdadeiro (visibilidade fechada para quem vê) vêm só
/// id, handle, apelido e foto; o resto fica nulo/vazio.
/// </summary>
public class PlayerProfileResponse
{
    /// <summary>Id da conta (a SPEC-20261108-1637 usa em pedidos de amizade).</summary>
    public Guid Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    /// <summary>Apelido da conta; sem apelido, o primeiro nome (nunca o nome completo).</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Foto da conta (a mesma que vale nos clubes — SPEC-20261029-1725).</summary>
    public string? PhotoUrl { get; set; }
    /// <summary>Quem está vendo é a própria dona do perfil (sem <c>?preview</c>).</summary>
    public bool IsOwner { get; set; }
    /// <summary>A visibilidade escondeu o perfil de quem vê: a tela mostra "perfil privado".</summary>
    public bool IsPrivate { get; set; }
    /// <summary>A resposta foi calculada como para um desconhecido (<c>?preview=stranger</c>).</summary>
    public bool IsPreview { get; set; }

    public string? City { get; set; }
    public string? State { get; set; }
    public string? Bio { get; set; }
    /// <summary>Home games em comum com quem vê (dono, membro ou inscrição vinculada dos dois lados).</summary>
    public List<PlayerProfileHomeGame> CommonHomeGames { get; set; } = [];
    /// <summary>Números da temporada do ano corrente (só inscrições vinculadas em torneios finalizados); null no perfil privado.</summary>
    public PlayerSeasonStats? Season { get; set; }
    /// <summary>Conquistas: null até a SPEC de conquistas (a tela mostra "em breve").</summary>
    public List<string>? Achievements { get; set; }
    /// <summary>Amigos: null até a SPEC-20261108-1637 (a tela esconde a contagem).</summary>
    public int? FriendCount { get; set; }
}

public class PlayerProfileHomeGame
{
    public Guid Id { get; set; }
    /// <summary>Nome do clube; no principal de dono Mesa, "Torneios de &lt;organizador&gt;".</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Rankings ativos do clube com página pública (/r/{slug}), até 3.</summary>
    public List<PlayerProfileRanking> PublicRankings { get; set; } = [];
}

public class PlayerProfileRanking
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class PlayerSeasonStats
{
    public int Year { get; set; }
    public int Tournaments { get; set; }
    public int Wins { get; set; }
    /// <summary>Vezes na premiação (posição premiada).</summary>
    public int InTheMoney { get; set; }
    /// <summary>Melhor colocação (1 = campeão); null sem torneio finalizado com posição.</summary>
    public int? BestPosition { get; set; }
}

/// <summary><c>GET me/played-with</c>: uma conta que jogou os mesmos torneios que a conta logada.</summary>
public class PlayedWithResponse
{
    public Guid Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    /// <summary>Torneios em comum.</summary>
    public int TimesPlayed { get; set; }
    /// <summary>Data do torneio em comum mais recente.</summary>
    public DateTimeOffset LastPlayedAt { get; set; }
}
