using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Api.Middleware;

public class ErrorHandlingMiddleware
{
    public const string InternalErrorMessage = "Algo deu errado do nosso lado. Tente de novo em instantes.";

    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = exception switch
        {
            // Senha certa, e-mail ainda não confirmado: 403 com code para o front oferecer "Reenviar"
            EmailNotConfirmedException notConfirmed => (HttpStatusCode.Forbidden, notConfirmed.Message),
            // Login social (SPEC-20261023-1210): conta desativada → 403; conta confirmada que pede a senha → 409
            AccountDisabledException disabled => (HttpStatusCode.Forbidden, disabled.Message),
            ExternalLoginLinkRequiredException linkRequired => (HttpStatusCode.Conflict, linkRequired.Message),
            // Recusa por plano (SPEC-20261023-1205): ANTES de DomainException (ela herda dela) → 402
            PlanLimitException planLimit => (HttpStatusCode.PaymentRequired, planLimit.Message),
            // Senha errada demais (login/confirmação) ou cadastros demais por IP no dia: ANTES de DomainException
            // (herdam dela) → 429
            TooManyRequestsException tooMany => (HttpStatusCode.TooManyRequests, tooMany.Message),
            DomainException domainEx => (HttpStatusCode.BadRequest, domainEx.Message),
            // xmin mudou entre a leitura e a gravação: não vaza a mensagem crua do EF
            DbUpdateConcurrencyException => (HttpStatusCode.Conflict, "Outra pessoa alterou isso ao mesmo tempo. Tente de novo."),
            KeyNotFoundException => (HttpStatusCode.NotFound, "Recurso não encontrado."),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Acesso não autorizado."),
            InvalidOperationException opEx => (HttpStatusCode.Conflict, opEx.Message),
            // A mensagem da exceção (e das internas) fica só no log: no corpo do 500 nunca vai detalhe interno
            _ => (HttpStatusCode.InternalServerError, InternalErrorMessage)
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Erro não tratado: {Message}", exception.Message);
        }
        else
        {
            _logger.LogWarning("Erro de negócio: {StatusCode} - {Message}", (int)statusCode, message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;
        if (exception is TooManyRequestsException wait)
            context.Response.Headers.RetryAfter = wait.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Erros com código de máquina (ex.: "email_not_confirmed", "account_disabled") levam o campo code no corpo
        var code = exception switch
        {
            EmailNotConfirmedException enc => enc.Code,
            AccountDisabledException ad => ad.Code,
            _ => null
        };
        object response = exception is ExternalLoginLinkRequiredException link
            // 409 do login social: o site pede a senha da conta e conclui com POST auth/login { linkTicket }
            ? new
            {
                status = (int)statusCode,
                error = statusCode.ToString(), // "Conflict"
                code = link.Code,
                ticket = link.Ticket,
                email = link.Email,
                message,
                timestamp = DateTimeOffset.UtcNow
            }
            : exception is TooManyRequestsException limited
            // 429 do login/confirmação/cadastro: o site mostra a mensagem (quanto esperar) e pode contar o retryAfter
            ? new
            {
                status = (int)statusCode,
                error = statusCode.ToString(), // "TooManyRequests"
                code = limited.Code,
                retryAfter = limited.RetryAfterSeconds,
                message,
                timestamp = DateTimeOffset.UtcNow
            }
            : exception is PlanLimitException plan
            // Contrato único do 402 (o UpgradeModal lê exatamente estes campos; nulos vão como null)
            ? new
            {
                status = (int)statusCode,
                error = statusCode.ToString(), // "PaymentRequired"
                code = plan.Code,
                feature = plan.Feature,
                currentPlan = plan.CurrentPlan,
                requiredPlan = plan.RequiredPlan,
                usage = plan.Usage,
                limit = plan.Limit,
                nextAllowedAt = plan.NextAllowedAt,
                message,
                timestamp = DateTimeOffset.UtcNow
            }
            : code is null
            ? new
            {
                status = (int)statusCode,
                error = statusCode.ToString(),
                message,
                timestamp = DateTimeOffset.UtcNow
            }
            : new
            {
                status = (int)statusCode,
                error = statusCode.ToString(),
                code,
                message,
                timestamp = DateTimeOffset.UtcNow
            };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
