namespace PokerTournament.Domain.Exceptions;

/// <summary>
/// "Definir senha" pedido de novo antes de o cooldown acabar (SPEC-20261023-1211, <c>POST me/password/setup-link</c>):
/// o ErrorHandlingMiddleware traduz em HTTP 429 <c>{ code: "too_many_requests", message, retryAfter }</c>.
/// </summary>
public sealed class TooManyPasswordSetupLinksException : TooManyRequestsException
{
    public const string ErrorCode = "too_many_requests";

    public override string Code => ErrorCode;

    public TooManyPasswordSetupLinksException(int retryAfterSeconds)
        : base(BuildMessage(retryAfterSeconds), retryAfterSeconds)
    {
    }

    private static string BuildMessage(int seconds)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(seconds / 60.0));
        var wait = minutes == 1 ? "1 minuto" : $"{minutes} minutos";
        return $"Já enviamos um link há pouco. Confira seu e-mail (e o spam) ou peça outro em {wait}.";
    }
}
