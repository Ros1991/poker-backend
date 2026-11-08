namespace PokerTournament.Domain.Entities;

/// <summary>
/// Notificação da conta (SPEC-20261108-1635): o que o sino do cabeçalho lista e o e-mail resumo junta. Pertence a uma
/// conta (<see cref="UserId"/>), tem tipo (<see cref="Enums.NotificationKind"/> via nameof), título, texto curto e um
/// link relativo do site (ex.: <c>/meus-torneios/{id}</c>). Não é soft-deletable de propósito: as lidas há mais de 90 dias
/// são apagadas de verdade pelo job (são avisos efêmeros, não dados do jogo).
/// </summary>
public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>Caminho relativo do site para onde o item leva (null = só informativo).</summary>
    public string? Link { get; set; }
    /// <summary>Quando a pessoa abriu/marcou como lida (null = não lida).</summary>
    public DateTimeOffset? ReadAt { get; set; }
    /// <summary>Quando entrou num e-mail resumo enviado (null = ainda não foi por e-mail). Evita repetir o mesmo item a cada hora.</summary>
    public DateTimeOffset? EmailedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
