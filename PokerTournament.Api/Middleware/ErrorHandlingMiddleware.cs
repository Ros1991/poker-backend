using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Api.Middleware;

public class ErrorHandlingMiddleware
{
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
            // Recusa por plano (SPEC-20261023-1205): ANTES de DomainException (ela herda dela) → 402
            PlanLimitException planLimit => (HttpStatusCode.PaymentRequired, planLimit.Message),
            DomainException domainEx => (HttpStatusCode.BadRequest, domainEx.Message),
            // xmin mudou entre a leitura e a gravação: não vaza a mensagem crua do EF
            DbUpdateConcurrencyException => (HttpStatusCode.Conflict, "Outra pessoa alterou isso ao mesmo tempo. Tente de novo."),
            KeyNotFoundException => (HttpStatusCode.NotFound, "Recurso não encontrado."),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Acesso não autorizado."),
            InvalidOperationException opEx => (HttpStatusCode.Conflict, opEx.Message),
            _ => (HttpStatusCode.InternalServerError, $"Erro: {exception.Message} | Inner: {exception.InnerException?.Message} | {exception.InnerException?.InnerException?.Message}")
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

        // Erros com código de máquina (ex.: "email_not_confirmed") levam o campo code no corpo
        var code = exception is EmailNotConfirmedException enc ? enc.Code : null;
        object response = exception is PlanLimitException plan
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
