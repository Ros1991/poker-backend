using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
            }
        };
    });

// Authorization
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("Admin"))
    .AddPolicy("TournamentDirector", policy => policy.RequireRole("Admin", "TournamentDirector"))
    .AddPolicy("Dealer", policy => policy.RequireRole("Admin", "TournamentDirector", "Dealer"));

// SignalR
builder.Services.AddSignalR();

// Swagger
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Poker Tournament API",
        Version = "v1",
        Description = "API para gerenciamento de torneios de poker"
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

// Application Services
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TournamentService>();
builder.Services.AddScoped<EntryService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<PrizeService>();
builder.Services.AddScoped<TimerService>();

// Controllers
builder.Services.AddControllers();

var app = builder.Build();

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

// Middleware de tratamento de erros
app.UseMiddleware<ErrorHandlingMiddleware>();

// Swagger (disponível também em produção para facilitar debug)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Poker Tournament API v1");
});

app.UseSerilogRequestLogging();

app.UseCors("AllowFrontend");

// Servir arquivos estáticos (uploads de fotos)
var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
if (!Directory.Exists(uploadsPath))
    Directory.CreateDirectory(uploadsPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<TournamentHub>("/hubs/tournament");

app.Run();
