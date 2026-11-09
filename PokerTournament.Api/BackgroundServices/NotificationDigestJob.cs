using PokerTournament.Application.Services;

namespace PokerTournament.Api.BackgroundServices;

/// <summary>
/// Primeiro hosted service do projeto (SPEC-20261108-1635): a cada 10 minutos manda o e-mail resumo das notificações
/// (<see cref="NotificationDigestService.RunAsync"/>, no máximo um por conta por hora) e apaga as lidas há mais de 90 dias
/// (<see cref="NotificationDigestService.CleanupAsync"/>). Espera 1 minuto depois do boot (o Migrate() já rodou; não disputa
/// o banco com a subida). Cada rodada tem escopo de DI próprio. Erro numa rodada vai para o log e a próxima acontece
/// normalmente. Instância única da API (Railway): sem trava distribuída — duas instâncias mandariam o resumo em dobro.
/// </summary>
public sealed class NotificationDigestJob : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NotificationDigestJob> _logger;

    public NotificationDigestJob(IServiceScopeFactory scopes, ILogger<NotificationDigestJob> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // encerramento normal da API
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var digest = scope.ServiceProvider.GetRequiredService<NotificationDigestService>();
            var now = DateTimeOffset.UtcNow;
            // Lembrete do dia do torneio (SPEC-20261108-1639) ANTES do resumo: o aviso novo já pode entrar no e-mail
            // desta rodada quando passar dos 5 min — e um erro nele não derruba o resumo
            try
            {
                await scope.ServiceProvider.GetRequiredService<TournamentReminderService>().RunAsync(now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Erro no lembrete do dia do torneio");
            }
            await digest.RunAsync(now, ct);
            var cleaned = await digest.CleanupAsync(now, ct);
            if (cleaned > 0)
                _logger.LogInformation("Notificações lidas há mais de 90 dias apagadas: {Count}", cleaned);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na rodada do e-mail resumo de notificações");
        }
    }
}
