namespace PokerTournament.Domain.Exceptions;

/// <summary>
/// Mensagens demais numa hora (SPEC-20261108-1638: 60 por conta). O ErrorHandlingMiddleware traduz em HTTP 429
/// <c>{ code: "too_many_messages", message, retryAfter }</c> com o cabeçalho <c>Retry-After</c>.
/// </summary>
public sealed class TooManyMessagesException : TooManyRequestsException
{
    public const string ErrorCode = "too_many_messages";

    public override string Code => ErrorCode;

    public TooManyMessagesException(int retryAfterSeconds)
        : base(BuildMessage(retryAfterSeconds), retryAfterSeconds)
    {
    }

    private static string BuildMessage(int seconds)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(seconds / 60.0));
        var wait = minutes == 1 ? "1 minuto" : $"{minutes} minutos";
        return $"Você mandou muitas mensagens na última hora. Espere {wait} para mandar de novo.";
    }
}

/// <summary>
/// Conversas novas demais no dia (SPEC-20261108-1638: 20 por conta). HTTP 429 <c>{ code: "too_many_conversations" }</c>.
/// </summary>
public sealed class TooManyConversationsException : TooManyRequestsException
{
    public const string ErrorCode = "too_many_conversations";

    public override string Code => ErrorCode;

    public TooManyConversationsException(int retryAfterSeconds)
        : base(BuildMessage(retryAfterSeconds), retryAfterSeconds)
    {
    }

    private static string BuildMessage(int seconds)
    {
        var hours = Math.Max(1, (int)Math.Ceiling(seconds / 3600.0));
        var wait = hours == 1 ? "1 hora" : $"{hours} horas";
        return $"Você abriu muitas conversas novas hoje. Espere {wait} para começar outra; as conversas que já existem continuam normais.";
    }
}

/// <summary>
/// Denúncias demais (revisão da onda 2, B2): a mesma mensagem ou a mesma conversa denunciada de novo pela mesma conta
/// em menos de 24 h, ou mais de 10 denúncias no dia. HTTP 429 <c>{ code: "too_many_reports" }</c>.
/// </summary>
public sealed class TooManyReportsException : TooManyRequestsException
{
    public const string ErrorCode = "too_many_reports";
    public const string DuplicateMessage = "Você já denunciou isso hoje. A Administração vai analisar a denúncia que já existe.";

    public override string Code => ErrorCode;

    public TooManyReportsException(int retryAfterSeconds, bool duplicate)
        : base(duplicate ? DuplicateMessage : BuildMessage(retryAfterSeconds), retryAfterSeconds)
    {
    }

    private static string BuildMessage(int seconds)
    {
        var hours = Math.Max(1, (int)Math.Ceiling(seconds / 3600.0));
        var wait = hours == 1 ? "1 hora" : $"{hours} horas";
        return $"Você fez muitas denúncias hoje. Espere {wait} para denunciar de novo.";
    }
}
