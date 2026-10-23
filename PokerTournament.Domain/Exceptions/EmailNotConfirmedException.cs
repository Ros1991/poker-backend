namespace PokerTournament.Domain.Exceptions;

/// <summary>
/// Login com a senha certa, mas o e-mail da conta ainda não foi confirmado pelo link do cadastro.
/// O ErrorHandlingMiddleware traduz em 403 com <c>{ code: "email_not_confirmed", message }</c> —
/// e-mail ou senha errados continuam 400 neutro (<see cref="DomainException"/>), sem revelar nada.
/// Não herda de DomainException de propósito: quem trata DomainException como 400 não pode engolir esta.
/// </summary>
public class EmailNotConfirmedException : Exception
{
    public const string ErrorCode = "email_not_confirmed";

    public string Code => ErrorCode;

    public EmailNotConfirmedException(string message) : base(message)
    {
    }
}
