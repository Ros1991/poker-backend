using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PokerTournament.Api.Authorization;
using PokerTournament.Api.Development;
using PokerTournament.Api.Http;
using PokerTournament.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PokerTournament.Api.Hubs;
using PokerTournament.Api.Middleware;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Mappings;
using PokerTournament.Application.Services;
using PokerTournament.Infrastructure.Data;
using Serilog;

// Permitir DateTimeOffset com offset não-UTC no Npgsql
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Railway injeta PORT — fazer o Kestrel escutar nesse port
var railwayPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(railwayPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");
}

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// DbContext com PostgreSQL
// Aceita DATABASE_URL (formato postgresql://user:pass@host:port/db) ou ConnectionStrings:DefaultConnection
string? connectionString;
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrWhiteSpace(databaseUrl))
{
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':', 2);
    connectionString =
        $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};" +
        $"Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo.Length > 1 ? userInfo[1] : string.Empty)};" +
        "SSL Mode=Require;Trust Server Certificate=true";
    var dbSchema = Environment.GetEnvironmentVariable("DB_SCHEMA");
    if (!string.IsNullOrWhiteSpace(dbSchema))
        connectionString += $";Search Path={dbSchema}";
}
else
{
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
}

var schemaForMigrations = Environment.GetEnvironmentVariable("DB_SCHEMA") ?? "public";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npg =>
        npg.MigrationsHistoryTable("__EFMigrationsHistory", schemaForMigrations)));

builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

// Authentication JWT
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT Secret não configurado.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero
        };

        // Permitir token via query string para SignalR
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            // Revalida a cada requisição (com cache curto):
            // - token de dealer (sub = id do pedido aprovado, cache de 30s): pedido
            //   desconectado, QR revogado, prazo vencido ou torneio terminado derrubam o
            //   acesso (gerar um QR novo NÃO derruba quem já foi aprovado);
            // - token de usuário com "sid" (cache de 60s): logout no aparelho, sessão revogada
            //   (troca de senha/papel, desativação) ou usuário inativo derrubam o access token
            //   de 12h. Token sem "sid" (emitido antes das sessões por aparelho) passa.
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                if (principal is null)
                    return;
                var sub = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? principal.FindFirst("sub")?.Value;
                var ct = context.HttpContext.RequestAborted;

                var kind = principal.FindFirst(TokenKinds.ClaimType)?.Value;
                if (kind == TokenKinds.Dealer)
                {
                    var access = context.HttpContext.RequestServices.GetRequiredService<DealerAccessService>();
                    if (!Guid.TryParse(sub, out var requestId) || !await access.IsActiveAsync(requestId, ct))
                        context.Fail("Acesso de dealer revogado ou expirado.");
                    return;
                }
                if (!string.IsNullOrEmpty(kind))
                    return;

                var sid = AuthService.GetSessionId(principal);
                if (sid is null)
                    return;
                var auth = context.HttpContext.RequestServices.GetRequiredService<AuthService>();
                if (!Guid.TryParse(sub, out var userId) || !await auth.IsSessionActiveAsync(sid.Value, userId, ct))
                    context.Fail("Sessão encerrada. Entre de novo.");
            }
        };
    });

// Authorization
// Todas as políticas carregam o TokenKindRequirement: token de usuário passa como
// sempre; token de dealer só passa em endpoint com [AllowTokenKind] do torneio dele.
builder.Services.AddSingleton<IAuthorizationHandler, TokenKindHandler>();
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new TokenKindRequirement())
        .Build())
    .AddPolicy("Admin", policy => policy.RequireRole("Admin").AddRequirements(new TokenKindRequirement()))
    .AddPolicy("TournamentDirector", policy => policy
        .RequireRole("Admin", "TournamentDirector").AddRequirements(new TokenKindRequirement()))
    .AddPolicy("Dealer", policy => policy
        .RequireRole("Admin", "TournamentDirector", "Dealer").AddRequirements(new TokenKindRequirement()));

// Limite por IP nos endpoints anônimos que criam coisas (TV nova, pedido de acesso do dealer,
// esqueci/redefinir senha, confirmar e-mail / reenviar confirmação). O IP vem de ClientIp.Resolve (valor mais à direita do
// X-Forwarded-For — o único que o cliente não controla; os da esquerda são forjáveis e
// dariam uma partição nova a cada requisição).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-anon", httpContext =>
    {
        var ip = ClientIp.Resolve(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
        });
    });

    // Busca de conta por e-mail (GET /Users/lookup): 10/min por usuário logado (claim sub /
    // NameIdentifier), caindo para o IP se o token não trouxer o claim. Freia quem tentaria
    // descobrir quais e-mails têm conta. Mesmo 429 (sem corpo) da política public-anon.
    // Depende de app.UseRateLimiter() vir DEPOIS de UseAuthentication (senão o User está vazio).
    options.AddPolicy("user-lookup", httpContext =>
    {
        var sub = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;
        var partition = !string.IsNullOrWhiteSpace(sub)
            ? "user:" + sub
            : "ip:" + ClientIp.Resolve(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
        });
    });
});

// SignalR. Dealer por QR: o registro guarda as conexões abertas de cada pedido aprovado
// (desconectar o dealer / desativar o QR derruba a conexão) e o filtro revalida o acesso
// do dealer em toda chamada de método do hub
builder.Services.AddSingleton<DealerConnectionRegistry>();
builder.Services.AddSingleton<DealerHubFilter>();
builder.Services.AddSignalR()
    .AddHubOptions<TournamentHub>(options => options.AddFilter<DealerHubFilter>());

// Swagger
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Poker da Galera API",
        Version = "v1",
        Description = "API do Poker da Galera — gestão de home games de poker"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Insira o token JWT no formato: Bearer {token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var envOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS");
        var origins = !string.IsNullOrWhiteSpace(envOrigins)
            ? envOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
              ?? ["http://localhost:3000", "http://localhost:5173"];

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// AutoMapper
builder.Services.AddAutoMapper(typeof(MappingProfile));

// Infrastructure Services
builder.Services.AddScoped<IWhatsAppProvider, PokerTournament.Infrastructure.Services.WhatsAppService>();

// Endereço público do frontend (links do e-mail): PUBLIC_APP_URL, senão PublicApp:Url, senão localhost
var publicAppUrl = Environment.GetEnvironmentVariable("PUBLIC_APP_URL")
    ?? builder.Configuration["PublicApp:Url"];
if (string.IsNullOrWhiteSpace(publicAppUrl))
{
    publicAppUrl = PublicAppOptions.DefaultUrl;
    if (!builder.Environment.IsDevelopment())
        Log.Warning("PUBLIC_APP_URL ausente: links do e-mail apontarão para {Url}", publicAppUrl);
}
builder.Services.Configure<PublicAppOptions>(o => o.Url = publicAppUrl.TrimEnd('/'));

// E-mail: Resend quando há RESEND_APITOKEN fora de Development (ou Email:ForceResend=true);
// senão só registra no log (LoggingEmailSender escreve o link — serve para testar sem Resend).
var resendToken = Environment.GetEnvironmentVariable("RESEND_APITOKEN")
    ?? builder.Configuration["Email:ResendApiToken"];
var forceResend = builder.Configuration.GetValue<bool>("Email:ForceResend");
var hasResendToken = !string.IsNullOrWhiteSpace(resendToken);
var useResend = hasResendToken && (!builder.Environment.IsDevelopment() || forceResend);
builder.Services.Configure<EmailOptions>(o =>
{
    o.ResendApiToken = resendToken;
    o.From = builder.Configuration["Email:From"] is { Length: > 0 } from ? from : EmailOptions.DefaultFrom;
    o.ForceResend = forceResend;
});
if (useResend)
{
    builder.Services.AddHttpClient<PokerTournament.Infrastructure.Services.ResendEmailSender>(
        c => c.Timeout = TimeSpan.FromSeconds(10));
    builder.Services.AddScoped<IEmailSender>(sp =>
        sp.GetRequiredService<PokerTournament.Infrastructure.Services.ResendEmailSender>());
}
else
{
    builder.Services.AddScoped<IEmailSender, PokerTournament.Infrastructure.Services.LoggingEmailSender>();
    // Fora de Development isso é defeito de configuração: ninguém recebe o link de senha
    if (!builder.Environment.IsDevelopment() && !hasResendToken)
        Log.Error("RESEND_APITOKEN ausente: nenhum e-mail é enviado (os links de redefinição de senha e de confirmação de e-mail ficam só no log do servidor)");
}

// Login social (SPEC-20261023-1210): Client ID público do Google em Auth:Google:ClientId (default no
// appsettings.json, sobrescrevível por Auth__Google__ClientId); Auth:Apple:ClientId vazio = Apple desligada (D17).
var externalLogin = new ExternalLoginOptions
{
    GoogleClientId = builder.Configuration[ExternalLoginOptions.GoogleClientIdKey],
    AppleClientId = builder.Configuration[ExternalLoginOptions.AppleClientIdKey],
};
builder.Services.Configure<ExternalLoginOptions>(o =>
{
    o.GoogleClientId = externalLogin.GoogleClientId;
    o.AppleClientId = externalLogin.AppleClientId;
});
// Nonce assinado (sem estado por emissão) + nonces gastos e tickets de vínculo em memória: pressupõe UMA instância
// da API (verdade hoje no Railway)
builder.Services.AddSingleton<ExternalLoginStore>();
// Senhas erradas em POST auth/login: 10 por IP + e-mail, 50 por e-mail e 50 por IP a cada 15 min (só as erradas contam)
builder.Services.AddSingleton<LoginAttemptLimiter>();
// Cadastros por IP no dia (POST auth/register manda e-mail): padrão 20 a cada 24 h (Registration:MaxPerIpPerDay)
builder.Services.AddSingleton<RegistrationLimiter>();
builder.Services.AddHttpClient(OidcTokenValidator.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
// Emissor de tokens de teste (chave local): existe SÓ em Development — fora dele nem entra no DI, e o
// validador só confia nele quando o recebe daqui
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<DevelopmentTestTokenIssuer>();
builder.Services.AddSingleton(sp => new OidcTokenValidator(
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetService<DevelopmentTestTokenIssuer>(),
    sp.GetRequiredService<ILogger<OidcTokenValidator>>()));
builder.Services.AddScoped<ExternalLoginEmailService>();

// Application Services
builder.Services.AddSingleton<JwtTokenFactory>();
// Planos Mesa/Clube/Liga (SPEC-20261023-1205): limites e recursos pelo plano efetivo do dono
builder.Services.AddScoped<PlanService>();
builder.Services.AddScoped<EmailConfirmationService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<DealerAccessService>();
builder.Services.AddScoped<DisplayService>();
builder.Services.AddScoped<TournamentService>();
builder.Services.AddScoped<EntryService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<PrizeService>();
builder.Services.AddScoped<TimerService>();
builder.Services.AddScoped<TournamentHistoryService>();
builder.Services.AddScoped<RankingLeaderboardService>();
builder.Services.AddScoped<RankingSlugService>();
// Fotos de pessoa e logo do clube no disco (SPEC-20261023-1213): pasta única de uploads para gravar e servir
var uploadsOptions = new UploadsOptions(UploadsOptions.ResolveRoot(builder.Environment.ContentRootPath));
builder.Services.AddSingleton(uploadsOptions);
builder.Services.AddScoped<PhotoService>();
builder.Services.AddScoped<TournamentPlayerService>();

// Cache curto do ranking público (link compartilhado por WhatsApp)
builder.Services.AddMemoryCache();

// Controllers
// SEC-01: filtro global de isolamento multi-tenant (Security:EnforceTenantIsolation, default true)
builder.Services.AddControllers(options =>
{
    options.Filters.Add<PokerTournament.Api.Filters.TenantAccessFilter>();
    // SPEC-20261023-1205: [RequirePlanFeature] — DEPOIS do isolamento (primeiro "é seu?", depois
    // "seu plano permite?"). Chave de emergência: Plans:Enforce (Plans__Enforce=false desliga tudo)
    options.Filters.Add<PokerTournament.Api.Filters.PlanFeatureFilter>();
    // Dealer por QR: tira contato/documento de jogador de qualquer resposta
    options.Filters.Add<PokerTournament.Api.Filters.ScopedTokenContactFilter>();
});

var app = builder.Build();

if (bool.TryParse(app.Configuration[PlanService.EnforceKey], out var enforcePlans) && !enforcePlans)
    Log.Warning("Plans:Enforce = false: nenhuma checagem de plano (limites e recursos pagos) está valendo");

foreach (var provider in ExternalLoginProviders.All)
{
    if (externalLogin.ClientIdFor(provider.Provider) is { } clientId)
        Log.Information("Login com {Provider} ligado (Client ID {ClientId})", provider.DisplayName, clientId);
    else
        Log.Information("Login com {Provider} desligado (Client ID não configurado)", provider.DisplayName);
}
if (app.Environment.IsDevelopment())
    Log.Warning("Development: emissor de tokens de teste do login social ATIVO (POST /api/v1/auth/external/test-token)");

// Aplicar migrations automaticamente em produção
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        var envSchema = Environment.GetEnvironmentVariable("DB_SCHEMA");
        if (!string.IsNullOrWhiteSpace(envSchema))
        {
            db.Database.ExecuteSqlRaw($"CREATE SCHEMA IF NOT EXISTS \"{envSchema}\"");
        }
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Erro ao aplicar migrations");
    }
}

// Endereço público dos rankings: gera o slug de quem ainda não tem (idempotente).
// Bloco separado de propósito: em prod (DB_SCHEMA=poker) o Migrate() acima lança
// PendingModelChangesWarning e não pode impedir este passo.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var slugs = scope.ServiceProvider.GetRequiredService<RankingSlugService>();
        var assigned = await slugs.AssignMissingAsync();
        if (assigned > 0)
            Log.Information("Endereço público gerado para {Count} ranking(s)", assigned);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Erro ao gerar endereço público dos rankings");
    }
}

// Middleware de tratamento de erros
app.UseMiddleware<ErrorHandlingMiddleware>();

// Swagger (disponível também em produção para facilitar debug)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Poker da Galera API v1");
});

app.UseSerilogRequestLogging();

app.UseCors("AllowFrontend");

// Servir arquivos estáticos (fotos de pessoa e logo do clube) da MESMA pasta em que o upload grava
// (UploadsOptions: UPLOADS_PATH, senão ContentRootPath/uploads — em produção o volume /app/uploads).
// Cada envio gera um nome de arquivo novo, então photos/ e logos/ podem ter cache longo e imutável (SPEC-20261023-1213).
var uploadsPath = uploadsOptions.RootPath;
if (!Directory.Exists(uploadsPath))
    Directory.CreateDirectory(uploadsPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = UploadsOptions.RequestPath,
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        var path = ctx.Context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith($"{UploadsOptions.RequestPath}/{UploadsOptions.PhotosFolder}/", StringComparison.Ordinal)
            || path.StartsWith($"{UploadsOptions.RequestPath}/{UploadsOptions.LogosFolder}/", StringComparison.Ordinal))
            headers.CacheControl = "public, max-age=31536000, immutable";
    }
});

app.UseAuthentication();
app.UseAuthorization();
// Depois da autenticação/autorização: a política "user-lookup" particiona pelo usuário do
// token (HttpContext.User só existe a partir daqui) e 401/403 não gastam o balde.
app.UseRateLimiter();

app.MapControllers();
// Emissor de tokens de teste do login social: rotas SÓ em Development (SPEC-20261023-1210)
if (app.Environment.IsDevelopment())
    app.MapDevelopmentTestTokenEndpoints();
// Conexão aberta cai quando o JWT vence (o token de dealer dura no máximo 24h)
app.MapHub<TournamentHub>("/hubs/tournament", o => o.CloseOnAuthenticationExpiration = true);

app.Run();
