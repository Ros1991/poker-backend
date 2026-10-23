using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerTournament.Application.Common;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Infrastructure.Services;

/// <summary>
/// Envio pelo Resend via HTTP puro (POST https://api.resend.com/emails), sem pacote NuGet.
/// Falha de rede ou resposta não-2xx viram <see cref="EmailSendResult"/> com Sent=false —
/// nunca exceção para quem chamou.
/// </summary>
public class ResendEmailSender : IEmailSender
{
    private const string Endpoint = "https://api.resend.com/emails";

    private readonly HttpClient _http;
    private readonly EmailOptions _options;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient http, IOptions<EmailOptions> options, ILogger<ResendEmailSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ResendApiToken))
            return new EmailSendResult(false, null, "RESEND_APITOKEN ausente");

        var from = string.IsNullOrWhiteSpace(_options.From) ? EmailOptions.DefaultFrom : _options.From;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ResendApiToken);
            request.Content = JsonContent.Create(new ResendRequest(from, [message.To], message.Subject, message.Html, message.Text));

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = Summarize($"Resend respondeu {(int)response.StatusCode}: {body}");
                _logger.LogWarning("Falha ao enviar e-mail pelo Resend: {Error}", error);
                return new EmailSendResult(false, null, error);
            }

            string? id = null;
            try
            {
                id = JsonSerializer.Deserialize<ResendResponse>(body)?.Id;
            }
            catch (JsonException)
            {
                // Enviou; só não deu para ler o id
            }

            return new EmailSendResult(true, id, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            var error = Summarize($"Resend indisponível: {ex.Message}");
            _logger.LogWarning(ex, "Falha ao enviar e-mail pelo Resend: {Error}", error);
            return new EmailSendResult(false, null, error);
        }
    }

    private static string Summarize(string text) => text.Length <= 500 ? text : text[..500];

    private record ResendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text);

    private record ResendResponse([property: JsonPropertyName("id")] string? Id);
}
