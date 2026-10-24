namespace PokerTournament.Domain.Exceptions;

/// <summary>
/// Senha errada demais em <c>POST auth/login</c> (LoginAttemptLimiter, revisão da onda 5 da SPEC-20261023-1210). O
/// ErrorHandlingMiddleware traduz em HTTP 429 <c>{ code: "too_many_attempts", message, retryAfter }</c> com o cabeçalho
/// <c>Retry-After</c>. Herda de DomainException só para nunca virar 500; o middleware o trata antes dela.
/// </summary>
public sealed class TooManyLoginAttemptsException : DomainException
{
    public const string ErrorCode = "too_many_attempts";

    public string Code => ErrorCode;

    /// <summary>Segundos até a janela que estourou abrir de novo.</summary>
    public int RetryAfterSeconds { get; }

    public TooManyLoginAttemptsException(int retryAfterSeconds)
        : base(BuildMessage(retryAfterSeconds))
    {
        RetryAfterSeconds = Math.Max(1, retryAfterSeconds);
    }

    private static string BuildMessage(int seconds)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(seconds / 60.0));
        var wait = minutes == 1 ? "1 minuto" : $"{minutes} minutos";
        return $"Muitas tentativas com senha errada. Espere {wait} e tente de novo, ou use \"Esqueci minha senha\".";
    }
}
