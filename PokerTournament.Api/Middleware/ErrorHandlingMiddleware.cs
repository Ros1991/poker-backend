using System.Net;
using System.Text.Json;
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
            DomainException domainEx => (HttpStatusCode.BadRequest, domainEx.Message),
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

        var response = new
        {
            status = (int)statusCode,
            error = statusCode.ToString(),
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
