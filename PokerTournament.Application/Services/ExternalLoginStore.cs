using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace PokerTournament.Application.Services;

/// <summary>Dados do login social guardados até a pessoa digitar a senha da conta (409 link_requires_password).</summary>
public sealed record ExternalLinkTicket(
    string Provider,
    string Subject,
    string? ProviderEmail,
    string? Picture,
    Guid UserId,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Estado curto do login social (SPEC-20261023-1210): nonces (10 min, uso único) e tickets de vínculo (10 min,
/// uso único, no máximo 5 senhas erradas).
/// <para>
/// O nonce NÃO guarda nada ao ser emitido: é <c>versão + 16 bytes aleatórios + validade</c> assinados com HMAC-SHA256
/// por uma chave sorteada quando a API sobe (base64url, 76 caracteres). Pedir nonce em massa (o endpoint é anônimo)
/// não ocupa memória e não tira o botão do Google de ninguém. Só quando chega um ID token VÁLIDO com aquele nonce
/// (<see cref="ConsumeNonce"/>, depois da assinatura do provedor) o nonce entra no conjunto de "já usados" até vencer —
/// esse conjunto só cresce com tokens que o provedor assinou de verdade, que ninguém fabrica em massa.
/// </para>
/// <para>
/// Pressupõe UMA instância da API — verdade hoje no Railway; com mais de uma, a chave e os "já usados" viram
/// configuração/cache distribuído. Reiniciar a API troca a chave: o que estava no meio do caminho deixa de valer
/// (o site pede outro nonce e a pessoa clica de novo) e nenhum nonce de antes pode ser reaproveitado.
/// O consumo é atômico (<c>TryAdd</c>/<c>TryRemove</c>): duas requisições com o mesmo nonce/ticket ao mesmo tempo →
/// só uma passa.
/// </para>
/// </summary>
public sealed class ExternalLoginStore
{
    public static readonly TimeSpan NonceLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(10);
    public const int MaxTicketAttempts = 5;

    private const byte NonceVersion = 1;
    private const int NonceRandomBytes = 16;
    private const int NoncePayloadBytes = 1 + NonceRandomBytes + sizeof(long); // versão + aleatório + validade (ms)
    private const int NonceMacBytes = 32; // HMAC-SHA256 inteiro
    private const int NonceBytes = NoncePayloadBytes + NonceMacBytes; // 57 bytes → 76 caracteres base64url, sem sobra

    // Tetos de memória. Os dois conjuntos só crescem depois de um ID token válido do provedor (nonce gasto) ou de um
    // 409 de conta confirmada (ticket, um por login do provedor), então o teto é só a última barreira: passou dele,
    // limpa os vencidos e, se ainda estiver cheio, recusa (falha fechada — nunca aceita nonce sem marcar como usado).
    private const int MaxUsedNonces = 200_000;
    private const int MaxTickets = 50_000;

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedNonces = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TicketEntry> _tickets = new(StringComparer.Ordinal);
    private long _writes;

    private sealed class TicketEntry(ExternalLinkTicket ticket)
    {
        public ExternalLinkTicket Ticket { get; } = ticket;
        public int Attempts;
    }

    /// <summary>Nonce novo, assinado e sem estado no servidor. Sempre emite (não há memória para lotar).</summary>
    public (string Nonce, DateTimeOffset ExpiresAt) IssueNonce(TimeSpan? lifetime = null)
    {
        var expiresAt = DateTimeOffset.UtcNow + (lifetime ?? NonceLifetime);
        Span<byte> raw = stackalloc byte[NonceBytes];
        raw[0] = NonceVersion;
        RandomNumberGenerator.Fill(raw.Slice(1, NonceRandomBytes));
        BinaryPrimitives.WriteInt64BigEndian(raw.Slice(1 + NonceRandomBytes, sizeof(long)), expiresAt.ToUnixTimeMilliseconds());
        HMACSHA256.HashData(_key, raw[..NoncePayloadBytes], raw[NoncePayloadBytes..]);
        return (Base64Url.EncodeToString(raw), expiresAt);
    }

    /// <summary>
    /// Gasta o nonce. Falso = assinatura que não é desta API (inventado, adulterado, de antes de reiniciar), vencido
    /// ou já usado. Chamar só depois de validar o ID token do provedor.
    /// </summary>
    public bool ConsumeNonce(string? nonce)
    {
        if (!TryReadNonce(nonce, out var expiresAt) || expiresAt <= DateTimeOffset.UtcNow)
            return false;
        if (!HasRoom(_usedNonces, MaxUsedNonces))
            return false;
        // A forma base64url de 57 bytes é única (sem bits de sobra): o texto serve de chave
        return _usedNonces.TryAdd(nonce!, expiresAt);
    }

    /// <summary>
    /// Ticket de vínculo novo. Tickets anteriores do mesmo login (provedor + sub) deixam de valer:
    /// só o último 409 conclui o vínculo. Null = memória cheia (só com 50 mil 409 de contas distintas em 10 min).
    /// </summary>
    public string? IssueTicket(string provider, string subject, string? providerEmail, string? picture, Guid userId)
    {
        foreach (var (key, entry) in _tickets)
        {
            if (entry.Ticket.Provider == provider && entry.Ticket.Subject == subject)
                _tickets.TryRemove(key, out _);
        }
        if (!HasRoom(_tickets, MaxTickets))
            return null;
        var ticket = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        _tickets[ticket] = new TicketEntry(new ExternalLinkTicket(
            provider, subject, providerEmail, picture, userId, DateTimeOffset.UtcNow + TicketLifetime));
        return ticket;
    }

    /// <summary>Gasta o ticket (senha certa). Null = inexistente, já usado, vencido ou esgotado.</summary>
    public ExternalLinkTicket? TakeTicket(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket) || !_tickets.TryRemove(ticket, out var entry))
            return null;
        return entry.Ticket.ExpiresAt > DateTimeOffset.UtcNow ? entry.Ticket : null;
    }

    /// <summary>Senha errada com o ticket: na 5ª o ticket some (a pessoa entra com o Google de novo).</summary>
    public void RegisterFailedAttempt(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket) || !_tickets.TryGetValue(ticket, out var entry))
            return;
        if (Interlocked.Increment(ref entry.Attempts) >= MaxTicketAttempts)
            _tickets.TryRemove(ticket, out _);
    }

    /// <summary>Descarta o ticket sem usar (o site recomeçou o fluxo pelo Google).</summary>
    public void RevokeTicket(string? ticket)
    {
        if (!string.IsNullOrWhiteSpace(ticket))
            _tickets.TryRemove(ticket, out _);
    }

    /// <summary>Confere formato, versão e assinatura (tempo constante) e lê a validade gravada no nonce.</summary>
    private bool TryReadNonce(string? nonce, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (string.IsNullOrEmpty(nonce) || nonce.Length != Base64Url.GetEncodedLength(NonceBytes))
            return false;
        Span<byte> raw = stackalloc byte[NonceBytes];
        if (Base64Url.DecodeFromChars(nonce, raw, out var consumed, out var written) != System.Buffers.OperationStatus.Done
            || consumed != nonce.Length || written != NonceBytes || raw[0] != NonceVersion)
            return false;
        Span<byte> mac = stackalloc byte[NonceMacBytes];
        HMACSHA256.HashData(_key, raw[..NoncePayloadBytes], mac);
        if (!CryptographicOperations.FixedTimeEquals(mac, raw[NoncePayloadBytes..]))
            return false;
        var ms = BinaryPrimitives.ReadInt64BigEndian(raw.Slice(1 + NonceRandomBytes, sizeof(long)));
        if (ms <= 0 || ms > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            return false;
        expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        return true;
    }

    private bool HasRoom<T>(ConcurrentDictionary<string, T> map, int max)
    {
        // Limpeza oportunista (sem job): a cada 200 gravações ou quando encheu
        if (Interlocked.Increment(ref _writes) % 200 == 0 || map.Count >= max)
            PurgeExpired();
        return map.Count < max;
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, expiresAt) in _usedNonces)
            if (expiresAt <= now) _usedNonces.TryRemove(key, out _);
        foreach (var (key, entry) in _tickets)
            if (entry.Ticket.ExpiresAt <= now) _tickets.TryRemove(key, out _);
    }
}
