namespace PokerTournament.Domain.Exceptions;

/// <summary>
/// Senha errada demais em <c>POST auth/login</c> e em <c>POST auth/confirm-email</c> (LoginAttemptLimiter, revisão da
/// onda 5 da SPEC-20261023-1210 e D16.10 da SPEC-20261023-1209). O ErrorHandlingMiddleware traduz em HTTP 429
/// <c>{ code: "too_many_attempts", message, retryAfter }</c> com o cabeçalho <c>Retry-After</c>.
/// </summary>
public sealed class TooManyLoginAttemptsException : TooManyRequestsException
{
    public const string ErrorCode = "too_many_attempts";

    public override string Code => ErrorCode;

    public TooManyLoginAttemptsException(int retryAfterSeconds)
        : base(BuildMessage(retryAfterSeconds), retryAfterSeconds)
    {
    }

    private static string BuildMessage(int seconds)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(seconds / 60.0));
        var wait = minutes == 1 ? "1 minuto" : $"{minutes} minutos";
        return $"Muitas tentativas com senha errada. Espere {wait} e tente de novo, ou use \"Esqueci minha senha\".";
    }
}
