namespace PokerTournament.Domain.Exceptions;

/// <summary>
/// Login social numa conta desativada pela administração (SPEC-20261023-1210). O ErrorHandlingMiddleware
/// traduz em HTTP 403 <c>{ code: "account_disabled", message }</c>. Só sai depois de um ID token válido:
/// quem chega aqui provou que controla o e-mail/login do provedor, então não há o que esconder.
/// Não herda de DomainException de propósito (senão viraria 400).
/// </summary>
public class AccountDisabledException : Exception
{
    public const string ErrorCode = "account_disabled";
    public const string DefaultMessage = "Conta desativada. Fale com o suporte.";

    public string Code => ErrorCode;

    public AccountDisabledException(string message = DefaultMessage) : base(message)
    {
    }
}
