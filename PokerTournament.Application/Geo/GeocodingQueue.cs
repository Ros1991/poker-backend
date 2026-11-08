using System.Threading.Channels;

namespace PokerTournament.Application.Geo;

/// <summary>Um home game para geocodificar e em qual tentativa está (1 = a primeira).</summary>
public sealed record GeocodeJob(Guid HomeGameId, int Attempt);

/// <summary>
/// Fila única de geocodificação (SPEC-20261108-1634, decisão 2): quem salva um endereço só enfileira o id; o
/// <c>GeocodingWorker</c> (hosted service da Api) consome um por vez, então o Nominatim nunca recebe dois pedidos ao
/// mesmo tempo. Singleton em memória: o que estiver pendente num reinício é recuperado pela varredura do worker na
/// subida (home games com endereço e sem coordenadas).
/// </summary>
public class GeocodingQueue
{
    private readonly Channel<GeocodeJob> _channel = Channel.CreateUnbounded<GeocodeJob>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    private int _pending;

    /// <summary>Trabalhos enfileirados e ainda não concluídos (para diagnóstico e testes).</summary>
    public int Pending => Volatile.Read(ref _pending);

    public void Enqueue(Guid homeGameId, int attempt = 1)
    {
        Interlocked.Increment(ref _pending);
        if (!_channel.Writer.TryWrite(new GeocodeJob(homeGameId, attempt)))
            Interlocked.Decrement(ref _pending);
    }

    /// <summary>Enfileira de novo depois de <paramref name="delay"/> (nova tentativa espaçada), sem travar a fila.</summary>
    public void EnqueueLater(Guid homeGameId, int attempt, TimeSpan delay, CancellationToken ct)
    {
        Interlocked.Increment(ref _pending);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, ct);
                if (!_channel.Writer.TryWrite(new GeocodeJob(homeGameId, attempt)))
                    Interlocked.Decrement(ref _pending);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Decrement(ref _pending);
            }
        }, CancellationToken.None);
    }

    /// <summary>Leitura do consumidor único (o worker).</summary>
    public ChannelReader<GeocodeJob> Reader => _channel.Reader;

    /// <summary>O worker avisa que terminou um trabalho (sucesso ou desistência).</summary>
    public void MarkDone() => Interlocked.Decrement(ref _pending);
}
