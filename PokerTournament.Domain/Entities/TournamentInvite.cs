namespace PokerTournament.Domain.Entities;

/// <summary>
/// Convite do torneio para os jogadores (SPEC-20261108-1632): o QR/link que o painel e o terminal mostram. Quem lê abre
/// <c>/entrar-no-torneio/{code}</c>, vê os inscritos ainda sem conta e pede o vínculo ("sou eu" →
/// <see cref="PlayerLinkRequest"/>), que o organizador aprova. O convite NÃO inscreve ninguém: a inscrição continua com o
/// organizador. Um ativo por torneio, vale 24 h; renovar revoga o anterior (RevokedAt — os pedidos já feitos continuam).
/// </summary>
public class TournamentInvite : BaseEntity
{
    public Guid TournamentId { get; set; }
    /// <summary>8 caracteres do alfabeto sem ambiguidade (sem 0/O/1/I), digitável à mão.</summary>
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
