namespace PokerTournament.Domain.Exceptions;

/// <summary>
/// Limite de tentativas do próprio serviço (senha errada no login/confirmação, cadastros por IP no dia). O
/// ErrorHandlingMiddleware traduz em HTTP 429 <c>{ code, message, retryAfter }</c> com o cabeçalho
/// <c>Retry-After</c>. Herda de DomainException só para nunca virar 500; o middleware o trata antes dela.
/// </summary>
public abstract class TooManyRequestsException : DomainException
{
    /// <summary>Código de máquina do 429 (ex.: "too_many_attempts", "too_many_registrations").</summary>
    public abstract string Code { get; }

    /// <summary>Segundos até a janela que estourou abrir de novo.</summary>
    public int RetryAfterSeconds { get; }

    protected TooManyRequestsException(string message, int retryAfterSeconds)
        : base(message)
    {
        RetryAfterSeconds = Math.Max(1, retryAfterSeconds);
    }
}

/// <summary>
/// Cadastro (<c>POST auth/register</c>) demais a partir do mesmo IP no dia (RegistrationLimiter, revisão da
/// SPEC-20261023-1209): o cadastro manda e-mail para qualquer endereço, então um IP não pode disparar e-mails sem fim.
/// </summary>
public sealed class TooManyRegistrationsException : TooManyRequestsException
{
    public const string ErrorCode = "too_many_registrations";
    public const string DefaultMessage =
        "Muitos cadastros feitos desta conexão hoje. Tente de novo mais tarde ou entre com o Google.";

    public override string Code => ErrorCode;

    public TooManyRegistrationsException(int retryAfterSeconds)
        : base(DefaultMessage, retryAfterSeconds)
    {
    }
}
