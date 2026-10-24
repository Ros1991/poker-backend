namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// <c>GET auth/confirm-email/{token}/info</c> (anônimo): o que a página /confirmar-email/:token mostra antes de pedir
/// a senha. Só o e-mail MASCARADO ("ro***@gmail.com") e se o link ainda vale; link que não vale (inexistente,
/// usado, revogado, vencido, conta já confirmada ou desativada) → <c>{ valid: false, email: null }</c>.
/// </summary>
public class ConfirmEmailInfoResponse
{
    public bool Valid { get; set; }
    public string? Email { get; set; }
}
