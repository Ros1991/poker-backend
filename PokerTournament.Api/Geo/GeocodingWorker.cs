using Microsoft.Extensions.Options;
using PokerTournament.Application.Geo;

namespace PokerTournament.Api.Geo;

/// <summary>
/// Consumidor único da <see cref="GeocodingQueue"/> (SPEC-20261108-1634, decisão 2): um trabalho por vez, cada um num
/// escopo próprio (<see cref="HomeGameGeocoder"/>). Falha transitória do provedor → nova tentativa depois de
/// <see cref="GeoOptions.RetryDelaysSeconds"/> (padrão 30 s e 5 min: 3 tentativas no total); esgotadas, marca a falha no
/// home game. Na subida, enfileira o que ficou pendente (endereço sem coordenadas) — a fila é só em memória.
/// </summary>
public class GeocodingWorker : BackgroundService
{
    private readonly GeocodingQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly GeoOptions _options;
    private readonly ILogger<GeocodingWorker> _logger;

    public GeocodingWorker(GeocodingQueue queue, IServiceScopeFactory scopes, IOptions<GeoOptions> options, ILogger<GeocodingWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnqueuePendingAsync(stoppingToken);

        var delays = _options.RetryDelays();
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var geocoder = scope.ServiceProvider.GetRequiredService<HomeGameGeocoder>();
                var outcome = await geocoder.ProcessAsync(job.HomeGameId, stoppingToken);
                if (outcome == GeocodeOutcome.Unavailable)
                {
                    // Tentativa n falhou: a próxima espera delays[n-1]; sem mais atraso configurado, desiste
                    if (job.Attempt - 1 < delays.Count)
                    {
                        var delay = delays[job.Attempt - 1];
                        _logger.LogInformation("Geocodificação do home game {HomeGameId}: tentativa {Attempt} falhou, nova em {Delay}", job.HomeGameId, job.Attempt, delay);
                        _queue.EnqueueLater(job.HomeGameId, job.Attempt + 1, delay, stoppingToken);
                    }
                    else
                    {
                        _logger.LogWarning("Geocodificação do home game {HomeGameId}: desistiu depois de {Attempts} tentativas", job.HomeGameId, job.Attempt);
                        await geocoder.MarkFailedAsync(job.HomeGameId, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Nunca derruba o worker: o próximo trabalho segue
                _logger.LogError(ex, "Erro ao geocodificar o home game {HomeGameId}", job.HomeGameId);
            }
            finally
            {
                _queue.MarkDone();
            }
        }
    }

    private async Task EnqueuePendingAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var geocoder = scope.ServiceProvider.GetRequiredService<HomeGameGeocoder>();
            var pending = await geocoder.PendingIdsAsync(ct);
            foreach (var id in pending)
                _queue.Enqueue(id);
            if (pending.Count > 0)
                _logger.LogInformation("Geocodificação: {Count} home game(s) com endereço sem coordenadas enfileirado(s) na subida", pending.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Erro ao buscar os home games pendentes de geocodificação");
        }
    }
}
