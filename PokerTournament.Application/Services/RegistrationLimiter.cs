using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Teto diário de cadastros por IP em <c>POST auth/register</c> (revisão da SPEC-20261023-1209). O cadastro é anônimo e
/// manda e-mail para qualquer endereço: além do limite por minuto da política "public-anon" (20/min por IP), cada IP
/// faz no máximo <see cref="MaxPerDay"/> cadastros por janela fixa de 24 h (padrão 20, <c>Registration:MaxPerIpPerDay</c>).
/// Conta todo cadastro que passa da validação (conta nova, repetida ou e-mail já em uso), então o teto também freia
/// quem testa quais e-mails têm conta. Em memória (some no restart, como o LoginAttemptLimiter); IPv6 conta por /64.
/// </summary>
public sealed class RegistrationLimiter
{
    public const string MaxPerDayKey = "Registration:MaxPerIpPerDay";
    public const int DefaultMaxPerDay = 20;
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private long _reservations;

    private sealed class Counter
    {
        public DateTimeOffset WindowStart;
        public int Count;
        public bool Removed;
    }

    public RegistrationLimiter(IConfiguration config)
    {
        MaxPerDay = int.TryParse(config[MaxPerDayKey], out var max) && max > 0 ? max : DefaultMaxPerDay;
    }

    public int MaxPerDay { get; }

    /// <summary>Gasta um cadastro do IP; o (MaxPerDay + 1)-ésimo da janela → <see cref="TooManyRegistrationsException"/> (429).</summary>
    public void Consume(string? ip)
    {
        var key = LoginAttemptLimiter.IpBucket(ip);
        if (Interlocked.Increment(ref _reservations) % 500 == 0)
            PurgeExpired();

        for (;;)
        {
            var now = DateTimeOffset.UtcNow;
            var counter = _counters.GetOrAdd(key, _ => new Counter { WindowStart = now });
            lock (counter)
            {
                if (counter.Removed)
                    continue; // a limpeza tirou este contador agora: pega o novo
                if (now - counter.WindowStart >= Window)
                {
                    counter.WindowStart = now;
                    counter.Count = 0;
                }
                if (counter.Count >= MaxPerDay)
                    throw new TooManyRegistrationsException((int)Math.Ceiling((counter.WindowStart + Window - now).TotalSeconds));
                counter.Count++;
                return;
            }
        }
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, counter) in _counters)
        {
            lock (counter)
            {
                if (now - counter.WindowStart < Window)
                    continue;
                counter.Removed = true;
                _counters.TryRemove(new KeyValuePair<string, Counter>(key, counter));
            }
        }
    }
}
