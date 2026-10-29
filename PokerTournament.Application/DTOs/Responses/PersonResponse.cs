namespace PokerTournament.Application.DTOs.Responses;

public class PersonResponse
{
    public Guid Id { get; set; }
    public Guid HomeGameId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    /// <summary>
    /// Foto EXIBIDA (SPEC-20261029-1725, D12): a da conta vinculada, se ela tiver; senão a do cadastro (PlayerPhoto).
    /// </summary>
    public string? PhotoUrl { get; set; }
    /// <summary>Foto do cadastro neste home game (a que o formulário troca/remove; vale quando a conta não tem foto).</summary>
    public string? OwnPhotoUrl { get; set; }
    /// <summary>A foto exibida vem da conta vinculada ("A foto vem da conta de Fulano").</summary>
    public bool PhotoFromAccount { get; set; }
    /// <summary>Conta vinculada (Person.UserId) e o nome dela. Zerados para o token de dealer (ScopedTokenContactFilter).</summary>
    public Guid? LinkedUserId { get; set; }
    public string? LinkedUserName { get; set; }
    public string? Document { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
