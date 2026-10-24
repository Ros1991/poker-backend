using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using PokerTournament.Application.Common;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Limite de senhas erradas em <c>POST auth/login</c> (revisão da onda 5, SPEC-20261023-1210). Conta só a tentativa
/// que termina em "E-mail ou senha inválidos." (e-mail inexistente, senha errada ou conta sem senha — os três iguais,
/// então o limite não revela quem tem conta); senha certa não conta, então quem entra várias vezes seguidas (dois
/// aparelhos, a mesa toda no mesmo Wi-Fi) nunca é barrado. Três janelas fixas de 15 min:
/// <list type="bullet">
/// <item><b>IP + e-mail</b>: 10 — quem tenta adivinhar a senha de uma conta;</item>
/// <item><b>e-mail</b> (qualquer IP): 50 — a mesma conta atacada de muitos IPs;</item>
/// <item><b>IP</b> (qualquer e-mail): 50 — um IP testando senhas em muitas contas.</item>
/// </list>
/// Pessoas diferentes no Wi-Fi do home game têm e-mails diferentes: uma errando não trava as outras.
/// A tentativa é RESERVADA antes de conferir a senha e devolvida se a senha estiver certa: requisições em paralelo
/// não passam todas pela checagem antes de alguma contar. IPv6 conta por /64 (quem tem um bloco não ganha uma janela
/// por endereço). Memória: a janela por IP é conferida primeiro e barra o resto, então cada IP cria no máximo ~100
/// chaves por janela; as vencidas saem numa limpeza a cada 500 reservas.
/// </summary>
public sealed class LoginAttemptLimiter
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public const int MaxPerIpAndEmail = 10;
    public const int MaxPerEmail = 50;
    public const int MaxPerIp = 50;

    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private long _reservations;

    internal sealed class Counter
    {
        public DateTimeOffset WindowStart;
        public int Count;
        /// <summary>Muda quando a janela recomeça: devolver uma reserva de uma janela antiga não mexe na nova.</summary>
        public long Generation;
        /// <summary>Saiu do dicionário na limpeza: quem ainda tinha a referência busca o contador novo.</summary>
        public bool Removed;
    }

    /// <summary>Tentativa reservada. <see cref="Succeeded"/> = não foi senha errada (devolve); sem chamar, ela conta.</summary>
    public sealed class Attempt
    {
        private readonly (Counter Counter, long Generation)[] _slots;
        private int _done;

        private Attempt((Counter, long)[] slots) => _slots = slots;

        internal static Attempt Of(List<(Counter, long)> slots) => new(slots.ToArray());

        /// <summary>
        /// A tentativa não terminou em senha errada (entrou, e-mail não confirmado, ticket ruim, erro inesperado):
        /// devolve a reserva. Com <paramref name="clearIpAndEmail"/> (entrou), zera também as senhas erradas da
        /// própria conta neste IP.
        /// </summary>
        public void Succeeded(bool clearIpAndEmail = false)
        {
            if (Interlocked.Exchange(ref _done, 1) == 1)
                return;
            for (var i = 0; i < _slots.Length; i++)
            {
                var (counter, generation) = _slots[i];
                lock (counter)
                {
                    if (counter.Generation != generation)
                        continue;
                    var isIpAndEmail = i == _slots.Length - 1 && _slots.Length == KeyCount;
                    if (clearIpAndEmail && isIpAndEmail)
                        counter.Count = 0;
                    else if (counter.Count > 0)
                        counter.Count--;
                }
            }
        }
    }

    private const int KeyCount = 3;

    /// <summary>
    /// Reserva uma tentativa nas três janelas (IP, e-mail, IP + e-mail, nessa ordem). Alguma cheia →
    /// <see cref="TooManyLoginAttemptsException"/> (429) SEM conferir a senha — senão quem ataca só trocaria o 400
    /// pelo 429 para saber quando acertou.
    /// </summary>
    public Attempt Reserve(string? ip, string? email)
    {
        var bucket = IpBucket(ip);
        var normalized = UserEmail.Normalize(email);
        var keys = new (string Key, int Max)[]
        {
            ("i:" + bucket, MaxPerIp),
            ("e:" + normalized, MaxPerEmail),
            ("ie:" + bucket + "|" + normalized, MaxPerIpAndEmail),
        };

        if (Interlocked.Increment(ref _reservations) % 500 == 0)
            PurgeExpired();

        var taken = new List<(Counter, long)>(KeyCount);
        foreach (var (key, max) in keys)
        {
            TimeSpan? retryAfter = null;
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
                        counter.Generation++;
                    }
                    if (counter.Count >= max)
                        retryAfter = counter.WindowStart + Window - now;
                    else
                    {
                        counter.Count++;
                        taken.Add((counter, counter.Generation));
                    }
                }
                break;
            }
            if (retryAfter is { } wait)
            {
                Attempt.Of(taken).Succeeded(); // devolve o que já tinha reservado
                throw new TooManyLoginAttemptsException((int)Math.Ceiling(wait.TotalSeconds));
            }
        }
        return Attempt.Of(taken);
    }

    /// <summary>
    /// IPv4 (inclusive o mapeado em IPv6) inteiro; IPv6 pelo prefixo /64; com porta ("1.2.3.4:5678", "[::1]:80") conta
    /// só o endereço; texto que não é IP como veio.
    /// </summary>
    public static string IpBucket(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return "desconhecido";
        var text = ip.Trim();
        if (!IPAddress.TryParse(text, out var address))
        {
            if (!IPEndPoint.TryParse(text, out var endPoint))
                return text.Length <= 64 ? text : text[..64];
            address = endPoint.Address;
        }
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
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
