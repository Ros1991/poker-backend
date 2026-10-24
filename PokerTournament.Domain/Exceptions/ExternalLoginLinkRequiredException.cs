namespace PokerTournament.Domain.Exceptions;

/// <summary>
/// Login social com o e-mail de uma conta já confirmada que ainda não tem esse login vinculado e
/// não pode ser vinculada sozinha (fora do <c>@gmail.com</c>, Google Workspace, Apple — SPEC-20261023-1210).
/// O ErrorHandlingMiddleware traduz em HTTP 409 <c>{ code: "link_requires_password", ticket, email, message }</c>:
/// o site pede a senha da conta e conclui o vínculo com <c>POST auth/login { email, password, linkTicket }</c>.
/// Não herda de DomainException de propósito (quem trata DomainException como 400 não pode engolir esta).
/// </summary>
public class ExternalLoginLinkRequiredException : Exception
{
    public const string ErrorCode = "link_requires_password";

    public string Code => ErrorCode;
    /// <summary>Ticket de vínculo (uso único, 10 min, em memória).</summary>
    public string Ticket { get; }
    /// <summary>E-mail da conta (o mesmo que o provedor informou — quem tem o token já o conhece).</summary>
    public string Email { get; }

    public ExternalLoginLinkRequiredException(string message, string ticket, string email) : base(message)
    {
        Ticket = ticket;
        Email = email;
    }
}
