using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Ai;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Api.Development;

/// <summary>
/// Testes do serviço de IA (SPEC-20261027-1312) — rotas SÓ em Development (o Program.cs só as mapeia lá) e só para o
/// Admin: apontar a URL base da Anthropic para o servidor falso dos testes e fazer uma chamada pelo <see cref="IAiService"/>
/// com o recurso pago de teste do catálogo de Development. Nunca com chave real nem contra a API real.
/// </summary>
public static class DevelopmentAiEndpoints
{
    public sealed class BaseUrlRequest
    {
        /// <summary>Ex.: http://127.0.0.1:9711 (vazio = volta para a URL de produção do SDK).</summary>
        public string? BaseUrl { get; set; }
    }

    public sealed class DevImage
    {
        public string Data { get; set; } = string.Empty;
        public string MediaType { get; set; } = "image/jpeg";
    }

    public sealed class DevMessageRequest
    {
        public string? Feature { get; set; }
        public string Prompt { get; set; } = string.Empty;
        public string? Model { get; set; }
        public string? System { get; set; }
        public int MaxTokens { get; set; } = 256;
        public Guid? HomeGameId { get; set; }
        public List<DevImage> Images { get; set; } = [];
    }

    public static void MapDevelopmentAiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/dev/ai")
            .RequireAuthorization("Admin")
            .WithTags("IA (somente Development)");

        group.MapPost("/base-url", (BaseUrlRequest request, AiRuntimeOptions options) =>
        {
            options.BaseUrl = request.BaseUrl;
            return Results.Ok(new { baseUrl = options.BaseUrl });
        });

        group.MapPost("/messages", async (DevMessageRequest request, IAiService ai, IAppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = Guid.TryParse((user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub"))?.Value, out var id) ? id : (Guid?)null;
            var name = userId is null ? null : await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (string?)u.FullName).FirstOrDefaultAsync(ct);
            try
            {
                var result = await ai.SendAsync(new AiRequest
                {
                    FeatureKey = string.IsNullOrWhiteSpace(request.Feature) ? AiFeatureCatalog.DevelopmentTestKey : request.Feature,
                    Prompt = request.Prompt,
                    Model = request.Model,
                    System = request.System,
                    MaxTokens = request.MaxTokens,
                    Images = request.Images.Select(i => new AiImage(Convert.FromBase64String(i.Data), i.MediaType)).ToList(),
                    Caller = new AiCaller(userId, name, request.HomeGameId),
                }, ct);
                return Results.Ok(new { ok = true, result });
            }
            catch (AiException ex)
            {
                return Results.Json(new { ok = false, code = ex.Code, message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
        });
    }
}
