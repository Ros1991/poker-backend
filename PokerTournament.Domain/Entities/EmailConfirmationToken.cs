namespace PokerTournament.Domain.Entities;

/// <summary>
/// Link "Confirme seu e-mail" enviado no cadastro: uso único, válido por 48 horas.
/// Mesmo formato do <see cref="PasswordResetToken"/> — só o hash SHA-256 do token fica no banco.
/// D16.10 (SPEC-20261023-1209): os dados do cadastro que gerou o link (nome, apelido, WhatsApp e o hash da
/// senha) ficam presos AO TOKEN e só vão para a conta quando ESTE token é confirmado com a senha certa —
/// repetir o cadastro com o e-mail de outra pessoa não muda a conta nem faz o link dela confirmar a senha
/// de quem repetiu. Só o token mais novo da conta guarda os dados (emitir outro limpa os anteriores).
/// </summary>
public class EmailConfirmationToken : BaseEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty; // SHA-256 (hex)
    public DateTimeOffset ExpiresAt { get; set; }         // criação + 48h
    public DateTimeOffset? UsedAt { get; set; }           // uso único
    public DateTimeOffset? RevokedAt { get; set; }        // substituído por um envio mais novo (ou conta já confirmada)
    public string? RequestedIp { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>
    /// O e-mail com este link saiu? Cadastro repetido dentro do cooldown de 2 min cria o token (com os dados
    /// novos) sem mandar e-mail; o cooldown só olha os tokens que foram enviados.
    /// </summary>
    public bool EmailSent { get; set; } = true;

    // Cadastro pendente (D16.10). Null = token sem cadastro preso (anterior à D16.10): confirma com a senha da conta.
    public string? PendingFullName { get; set; }
    public string? PendingNickname { get; set; }
    public string? PendingWhatsapp { get; set; }
    public string? PendingPasswordHash { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
