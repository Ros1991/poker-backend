using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// QR do dealer: um ativo por torneio, válido até o torneio terminar e nunca mais
/// que 24h. O código lido no QR é trocado por um JWT restrito ao terminal daquele
/// torneio (ver TokenKinds / AllowTokenKind na Api).
/// </summary>
public class DealerAccessService
{
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(24);
    // A validade é conferida a cada requisição do dealer; o cache segura o volume
    // e a revogação/novo QR grava "inativo" nele na hora (depois do commit).
    private static readonly TimeSpan ValidityCacheTtl = TimeSpan.FromSeconds(30);

    private const string InvalidQrMessage = "QR do dealer inválido ou expirado. Peça um novo ao organizador.";

    private readonly IAppDbContext _db;
    private readonly JwtTokenFactory _tokens;
    private readonly IMemoryCache _cache;

    public DealerAccessService(IAppDbContext db, JwtTokenFactory tokens, IMemoryCache cache)
    {
        _db = db;
        _tokens = tokens;
        _cache = cache;
    }

    /// <summary>Gera um QR novo para o torneio e revoga o anterior (quem usava cai).</summary>
    public async Task<DealerAccessResponse> CreateAsync(Guid tournamentId, Guid? userId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        if (IsOver(tournament.Status))
            throw new DomainException("O torneio já terminou: não dá para gerar o QR do dealer.");

        var now = DateTimeOffset.UtcNow;
        var revoked = await RevokeActiveAsync(tournamentId, now, ct);

        var access = new DealerAccess
        {
            TournamentId = tournamentId,
            Code = NewCode(),
            ExpiresAt = now + MaxLifetime,
            CreatedBy = userId,
        };
        _db.DealerAccesses.Add(access);
        await _db.SaveChangesAsync(ct);
        MarkRevoked(revoked);

        return Map(access);
    }

    /// <summary>QR ativo do torneio (para reexibir), ou null.</summary>
    public async Task<DealerAccessResponse?> GetActiveAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var access = await _db.DealerAccesses
            .AsNoTracking()
            .Where(a => a.TournamentId == tournamentId && a.RevokedAt == null && a.ExpiresAt > now)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return access is null ? null : Map(access);
    }

    public async Task RevokeAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var revoked = await RevokeActiveAsync(tournamentId, DateTimeOffset.UtcNow, ct);
        await _db.SaveChangesAsync(ct);
        MarkRevoked(revoked);
    }

    /// <summary>Troca o código do QR pelo token do dealer.</summary>
    public async Task<DealerExchangeResponse> ExchangeAsync(string? code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
            throw new DomainException(InvalidQrMessage);

        var now = DateTimeOffset.UtcNow;
        var access = await _db.DealerAccesses.AsNoTracking().FirstOrDefaultAsync(a => a.Code == code, ct);
        if (access is null || access.RevokedAt is not null || access.ExpiresAt <= now)
            throw new DomainException(InvalidQrMessage);

        // O filtro de soft-delete esconde torneio excluído
        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == access.TournamentId, ct);
        if (tournament is null)
            throw new DomainException(InvalidQrMessage);
        if (IsOver(tournament.Status))
            throw new DomainException("O torneio já terminou.");

        // Vários dealers lendo ao mesmo tempo: contador sem disputa de xmin
        await _db.DealerAccesses
            .Where(a => a.Id == access.Id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.LastExchangeAt, now)
                .SetProperty(a => a.ExchangeCount, a => a.ExchangeCount + 1), ct);

        return new DealerExchangeResponse
        {
            AccessToken = _tokens.CreateDealerToken(access),
            ExpiresAt = access.ExpiresAt,
            TournamentId = tournament.Id,
            TournamentName = tournament.Name,
        };
    }

    /// <summary>O acesso ainda vale? (não revogado, no prazo e torneio não terminado)</summary>
    public async Task<bool> IsActiveAsync(Guid accessId, CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(CacheKey(accessId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ValidityCacheTtl;
            var now = DateTimeOffset.UtcNow;
            var access = await _db.DealerAccesses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accessId, ct);
            if (access is null || access.RevokedAt is not null || access.ExpiresAt <= now)
                return false;
            var status = await _db.Tournaments
                .Where(t => t.Id == access.TournamentId)
                .Select(t => t.Status)
                .FirstOrDefaultAsync(ct);
            return status is not null && !IsOver(status);
        });
    }

    public async Task<DealerSessionResponse?> GetSessionAsync(Guid accessId, CancellationToken ct = default)
    {
        var access = await _db.DealerAccesses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accessId, ct);
        if (access is null)
            return null;
        var name = await _db.Tournaments
            .Where(t => t.Id == access.TournamentId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);
        return name is null ? null : new DealerSessionResponse
        {
            TournamentId = access.TournamentId,
            TournamentName = name,
            ExpiresAt = access.ExpiresAt,
        };
    }

    // Só marca em memória; o chamador salva e DEPOIS chama MarkRevoked
    private async Task<List<Guid>> RevokeActiveAsync(Guid tournamentId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await _db.DealerAccesses
            .Where(a => a.TournamentId == tournamentId && a.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var access in active)
            access.RevokedAt = now;
        return active.Select(a => a.Id).ToList();
    }

    // Depois do commit: grava "inativo" no cache. Limpar antes do SaveChanges deixava
    // uma requisição concorrente ler o acesso ainda não revogado e cachear "ativo" por 30s.
    private void MarkRevoked(IEnumerable<Guid> accessIds)
    {
        foreach (var id in accessIds)
            _cache.Set(CacheKey(id), false, ValidityCacheTtl);
    }

    private static bool IsOver(string status) =>
        status == nameof(TournamentStatus.Finished) || status == nameof(TournamentStatus.Cancelled);

    private static string CacheKey(Guid accessId) => $"dealer-access:{accessId}";

    // 128 bits aleatórios em base64url (22 caracteres): impossível de adivinhar
    private static string NewCode() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static DealerAccessResponse Map(DealerAccess access) => new()
    {
        Id = access.Id,
        Code = access.Code,
        ExpiresAt = access.ExpiresAt,
        CreatedAt = access.CreatedAt,
        ExchangeCount = access.ExchangeCount,
    };
}
