using System.Buffers;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// QR do dealer com autorização do organizador. O QR (um ativo por torneio, válido até o
/// torneio terminar e nunca mais que 24h) não dá acesso sozinho: quem lê informa o nome e
/// cria um <see cref="DealerRequest"/>; o organizador autoriza ou recusa no painel, e só o
/// aparelho que fez o pedido (deviceKey) recebe o JWT restrito ao terminal daquele torneio
/// (ver TokenKinds / AllowTokenKind na Api). O sub do token é o id do pedido: revogar um
/// pedido derruba só aquele aparelho.
///
/// Plano (SPEC-20261023-1205, D5 — dealer por QR só no pago): gerar/ver/revogar o QR e as listas de
/// pedidos passam pelo [RequirePlanFeature(DealerQr)] do controller; aqui, nos caminhos anônimos do
/// aparelho, o pedido (RequestAsync) e a emissão do token (GetStatusAsync) recusam com 402
/// feature_locked se o dono caiu para o Mesa. IsActiveAsync NÃO confere plano: o dealer já aprovado
/// continua operando o torneio criado (só pagamentos/mesas caem no 402 pelo filtro).
/// </summary>
public class DealerAccessService
{
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(24);
    /// <summary>Pedido sem decisão vence em 15 min (vira Rejected na leitura).</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(15);
    public const int MaxPendingPerTournament = 5;
    public const int NameMinLength = 2;
    public const int NameMaxLength = 40;
    // A validade é conferida a cada requisição do dealer; o cache segura o volume
    // e a revogação grava "inativo" nele na hora (depois do commit).
    private static readonly TimeSpan ValidityCacheTtl = TimeSpan.FromSeconds(30);
    // Pedidos recusados/vencidos somem depois de 7 dias (os aprovados ficam: o
    // histórico do torneio mostra "Dealer · Nome" a partir deles)
    private static readonly TimeSpan CleanupAge = TimeSpan.FromDays(7);

    private const string InvalidQrMessage = "QR do dealer inválido ou expirado. Peça um novo ao organizador.";

    private static readonly string Pending = nameof(DealerRequestStatus.Pending);
    private static readonly string Approved = nameof(DealerRequestStatus.Approved);
    private static readonly string Rejected = nameof(DealerRequestStatus.Rejected);
    private static readonly string Revoked = nameof(DealerRequestStatus.Revoked);

    private readonly IAppDbContext _db;
    private readonly JwtTokenFactory _tokens;
    private readonly IMemoryCache _cache;
    private readonly PlanService _plans;

    public DealerAccessService(IAppDbContext db, JwtTokenFactory tokens, IMemoryCache cache, PlanService plans)
    {
        _db = db;
        _tokens = tokens;
        _cache = cache;
        _plans = plans;
    }

    /// <summary>Mensagem do 402 no celular do dealer (ele não é o organizador).</summary>
    public static readonly string PlanLockedMessage =
        "O terminal do dealer por QR não está liberado neste torneio: é um recurso a partir do plano " +
        $"{PlanCatalog.MinimumPlanFor(PlanFeature.DealerQr)}. Avise o organizador.";

    // D5 nos caminhos anônimos do aparelho (pedido e emissão do token); ninguém tem exceção de plano (SPEC-20261026-1126)
    private Task EnsureDealerQrAsync(Guid tournamentId, CancellationToken ct) =>
        _plans.EnsureFeatureForTournamentAsync(tournamentId, PlanFeature.DealerQr, message: PlanLockedMessage, ct: ct);

    // ------------------------------------------------------------ QR (organizador)

    /// <summary>
    /// Gera um QR novo para o torneio. O anterior deixa de valer para pedidos novos
    /// (SupersededAt), mas quem já foi aprovado por ele continua conectado.
    /// </summary>
    public async Task<DealerAccessResponse> CreateAsync(Guid tournamentId, Guid? userId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        if (IsOver(tournament.Status))
            throw new DomainException("O torneio já terminou: não dá para gerar o QR do dealer.");

        await CleanupOldRequestsAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var active = await _db.DealerAccesses
            .Where(a => a.TournamentId == tournamentId && a.RevokedAt == null && a.SupersededAt == null)
            .ToListAsync(ct);
        foreach (var old in active)
            old.SupersededAt = now;

        var access = new DealerAccess
        {
            TournamentId = tournamentId,
            Code = NewCode(),
            ExpiresAt = now + MaxLifetime,
            CreatedBy = userId,
        };
        _db.DealerAccesses.Add(access);
        await _db.SaveChangesAsync(ct);

        return await MapAccessAsync(access, ct);
    }

    /// <summary>QR ativo do torneio (para reexibir), ou null.</summary>
    public async Task<DealerAccessResponse?> GetActiveAsync(Guid tournamentId, CancellationToken ct = default)
    {
        await EnsureTournamentAsync(tournamentId, ct);
        var now = DateTimeOffset.UtcNow;
        var access = await _db.DealerAccesses
            .AsNoTracking()
            .Where(a => a.TournamentId == tournamentId && a.RevokedAt == null && a.SupersededAt == null && a.ExpiresAt > now)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return access is null ? null : await MapAccessAsync(access, ct);
    }

    /// <summary>
    /// Revoga o QR do torneio: o código deixa de valer E todo dealer aprovado por ele cai
    /// (pedidos aprovados viram Revoked, pendentes viram Rejected).
    /// </summary>
    public async Task RevokeAsync(Guid tournamentId, Guid? userId, CancellationToken ct = default)
    {
        await EnsureTournamentAsync(tournamentId, ct);
        var now = DateTimeOffset.UtcNow;
        var accesses = await _db.DealerAccesses
            .Where(a => a.TournamentId == tournamentId && a.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var access in accesses)
            access.RevokedAt = now;

        var requests = await _db.DealerRequests
            .Where(r => r.TournamentId == tournamentId && (r.Status == Pending || r.Status == Approved))
            .ToListAsync(ct);
        var dropped = new List<Guid>();
        foreach (var request in requests)
        {
            if (request.Status == Approved)
                dropped.Add(request.Id);
            request.Status = request.Status == Approved ? Revoked : Rejected;
            request.DecidedAt = now;
            request.DecidedBy = userId;
        }

        await _db.SaveChangesAsync(ct);
        MarkRevoked(dropped);
    }

    /// <summary>
    /// Exclusão do home game (SPEC-20261026-1126): revoga os QRs de todos os <paramref name="tournamentIds"/> e
    /// encerra os pedidos (aprovados viram Revoked, pendentes viram Rejected) — mesma regra do <see cref="RevokeAsync"/>,
    /// mas em UPDATE direto (sem disputa de xmin com o "último acesso" que o terminal grava) e SEM gravar o cache:
    /// roda dentro da transação de quem chama, que depois do commit passa <c>DroppedRequestIds</c> para
    /// <see cref="ForgetRevokedRequests"/>.
    /// </summary>
    public async Task<(int Accesses, int Requests, List<Guid> DroppedRequestIds)> RevokeForTournamentsAsync(
        IReadOnlyCollection<Guid> tournamentIds, Guid? userId, CancellationToken ct = default)
    {
        if (tournamentIds.Count == 0)
            return (0, 0, []);

        var now = DateTimeOffset.UtcNow;
        var accesses = await _db.DealerAccesses
            .Where(a => tournamentIds.Contains(a.TournamentId) && a.RevokedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.RevokedAt, now)
                .SetProperty(a => a.UpdatedAt, now), ct);

        var dropped = await _db.DealerRequests
            .Where(r => tournamentIds.Contains(r.TournamentId) && r.Status == Approved)
            .Select(r => r.Id)
            .ToListAsync(ct);
        var revoked = await _db.DealerRequests
            .Where(r => tournamentIds.Contains(r.TournamentId) && r.Status == Approved)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, Revoked)
                .SetProperty(r => r.DecidedAt, now)
                .SetProperty(r => r.DecidedBy, userId)
                .SetProperty(r => r.UpdatedAt, now), ct);
        var rejected = await _db.DealerRequests
            .Where(r => tournamentIds.Contains(r.TournamentId) && r.Status == Pending)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, Rejected)
                .SetProperty(r => r.DecidedAt, now)
                .SetProperty(r => r.DecidedBy, userId)
                .SetProperty(r => r.UpdatedAt, now), ct);

        return (accesses, revoked + rejected, dropped);
    }

    /// <summary>Depois do commit da revogação em lote: o terminal desses pedidos cai na próxima requisição.</summary>
    public void ForgetRevokedRequests(IEnumerable<Guid> requestIds) => MarkRevoked(requestIds);

    // ------------------------------------------------------ pedido (aparelho, anônimo)

    /// <summary>
    /// Aparelho do dealer leu o QR e informou o nome. Reaproveita o pedido aprovado (no
    /// prazo) ou pendente do mesmo aparelho no mesmo torneio; senão cria um novo,
    /// respeitando o limite de pendentes por torneio.
    /// </summary>
    public async Task<DealerRequestCreatedResponse> RequestAsync(
        string? code, string? name, string? deviceKey, string? userAgent, CancellationToken ct = default)
    {
        // O código é base64url (NewCode): qualquer outro caractere (ex.: NUL, que o
        // PostgreSQL recusa em texto) já é QR inválido, sem chegar ao banco
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64 || !IsCodeFormat(code))
            throw new DomainException(InvalidQrMessage);

        var now = DateTimeOffset.UtcNow;
        var access = await _db.DealerAccesses.AsNoTracking().FirstOrDefaultAsync(a => a.Code == code, ct);
        if (access is null || access.RevokedAt is not null || access.SupersededAt is not null || access.ExpiresAt <= now)
            throw new DomainException(InvalidQrMessage);

        // O filtro de soft-delete esconde torneio excluído
        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == access.TournamentId, ct)
            ?? throw new DomainException(InvalidQrMessage);
        if (IsOver(tournament.Status))
            throw new DomainException("O torneio já terminou.");

        // D5: QR gerado quando o dono ainda era Clube não abre pedido depois que ele caiu para o Mesa
        await EnsureDealerQrAsync(tournament.Id, ct);

        var dealerName = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(deviceKey) || deviceKey.Length > 128)
            throw new DomainException("Chave do aparelho inválida. Recarregue a página e leia o QR de novo.");
        var deviceKeyHash = AuthService.HashToken(deviceKey);

        await ExpireStalePendingAsync(tournament.Id, now, ct);

        // Reabrir o terminal no mesmo aparelho dentro da validade não pede autorização de novo
        var approved = await _db.DealerRequests
            .AsNoTracking()
            .Where(r => r.TournamentId == tournament.Id && r.DeviceKeyHash == deviceKeyHash
                     && r.Status == Approved && r.ExpiresAt > now
                     && _db.DealerAccesses.Any(a => a.Id == r.DealerAccessId && a.RevokedAt == null))
            .OrderByDescending(r => r.DecidedAt)
            .FirstOrDefaultAsync(ct);
        if (approved is not null)
            return Created(approved, tournament.Name);

        // Mesmo aparelho pedindo de novo (recarregou a página, corrigiu o nome): vale o pedido
        // pendente que já existe — não gasta outra vaga das 5. Só nome e navegador mudam: a
        // janela de 15 min conta do PRIMEIRO pedido (renovar RequestedAt aqui deixava um
        // aparelho manter o pedido vivo para sempre reenviando o POST).
        var pending = await _db.DealerRequests
            .Where(r => r.TournamentId == tournament.Id && r.DeviceKeyHash == deviceKeyHash && r.Status == Pending)
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (pending is not null)
        {
            var agent = Truncate(userAgent);
            if (pending.Name != dealerName || pending.UserAgent != agent)
            {
                pending.Name = dealerName;
                pending.UserAgent = agent;
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Outro envio do mesmo aparelho gravou o pedido no mesmo instante: vale o dele
                }
            }
            return Created(pending, tournament.Name);
        }

        var pendingCount = await _db.DealerRequests
            .CountAsync(r => r.TournamentId == tournament.Id && r.Status == Pending, ct);
        if (pendingCount >= MaxPendingPerTournament)
            throw new DomainException(
                $"Já há {MaxPendingPerTournament} pedidos aguardando autorização neste torneio. " +
                "Peça ao organizador para autorizar ou recusar os pendentes e tente de novo.");

        var request = new DealerRequest
        {
            TournamentId = tournament.Id,
            DealerAccessId = access.Id,
            Name = dealerName,
            DeviceKeyHash = deviceKeyHash,
            Status = Pending,
            RequestedAt = now,
            UserAgent = Truncate(userAgent),
        };
        _db.DealerRequests.Add(request);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Dois envios simultâneos do mesmo aparelho (toque duplo, rede lenta): o índice
            // único parcial ux_dealer_requests_pending_device — (tournament_id,
            // device_key_hash) WHERE status = 'Pending' — deixa entrar um só. Vale o que entrou.
            _db.DealerRequests.Remove(request); // estava Added: só sai do rastreamento
            var existing = await _db.DealerRequests
                .AsNoTracking()
                .Where(r => r.TournamentId == tournament.Id && r.DeviceKeyHash == deviceKeyHash && r.Status == Pending)
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefaultAsync(ct)
                ?? throw new DomainException("Não foi possível registrar o pedido. Tente de novo.");
            return Created(existing, tournament.Name);
        }
        return Created(request, tournament.Name);
    }

    /// <summary>
    /// Estado do pedido para o aparelho. Null = pedido inexistente. Pendente há mais de
    /// 15 min vira Rejected aqui. O token só vem com Approved válido e deviceKey conferido.
    /// </summary>
    public async Task<DealerRequestStatusResponse?> GetStatusAsync(
        Guid requestId, string? deviceKey, CancellationToken ct = default)
    {
        var request = await _db.DealerRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var expired = false;
        if (request.Status == Pending && request.RequestedAt + PendingLifetime <= now)
        {
            await _db.DealerRequests
                .Where(r => r.Id == request.Id && r.Status == Pending)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, Rejected)
                    .SetProperty(r => r.DecidedAt, now)
                    .SetProperty(r => r.UpdatedAt, now), ct);
            request.Status = Rejected;
            request.DecidedAt = now;
            request.DecidedBy = null;
            expired = true;
        }
        else if (request.Status == Rejected && request.DecidedBy is null)
        {
            expired = true; // venceu sem ninguém decidir (lido antes por outra consulta)
        }
        else if (request.Status == Approved && (request.ExpiresAt is null || request.ExpiresAt <= now))
        {
            expired = true;
        }

        var tournament = await _db.Tournaments
            .AsNoTracking()
            .Where(t => t.Id == request.TournamentId)
            .Select(t => new { t.Name, t.Status })
            .FirstOrDefaultAsync(ct);
        if (tournament is null)
            return null;

        var response = new DealerRequestStatusResponse
        {
            RequestId = request.Id,
            Status = request.Status,
            Name = request.Name,
            TournamentId = request.TournamentId,
            TournamentName = tournament.Name,
            Expired = expired,
            ExpiresAt = request.Status == Approved ? request.ExpiresAt : null,
        };

        if (request.Status == Approved && !expired
            && !IsOver(tournament.Status)
            && !string.IsNullOrEmpty(deviceKey) && deviceKey.Length <= 128
            && AuthService.HashToken(deviceKey) == request.DeviceKeyHash
            && await _db.DealerAccesses.AnyAsync(a => a.Id == request.DealerAccessId && a.RevokedAt == null, ct))
        {
            // D5: aprovado antes de o dono cair para o Mesa, mas o token ainda não tinha saído → 402
            await EnsureDealerQrAsync(request.TournamentId, ct);
            response.AccessToken = _tokens.CreateDealerToken(request);
        }

        return response;
    }

    // ------------------------------------------------------------ organizador

    /// <summary>Pedidos pendentes (mais antigo primeiro) e dealers conectados do torneio.</summary>
    public async Task<List<DealerRequestResponse>> ListAsync(Guid tournamentId, CancellationToken ct = default)
    {
        await EnsureTournamentAsync(tournamentId, ct);
        var now = DateTimeOffset.UtcNow;
        await ExpireStalePendingAsync(tournamentId, now, ct);

        var rows = await _db.DealerRequests
            .AsNoTracking()
            .Where(r => r.TournamentId == tournamentId
                     && (r.Status == Pending
                         || (r.Status == Approved && r.ExpiresAt > now
                             && _db.DealerAccesses.Any(a => a.Id == r.DealerAccessId && a.RevokedAt == null))))
            .ToListAsync(ct);

        return rows
            .OrderBy(r => r.Status == Pending ? 0 : 1)
            .ThenBy(r => r.Status == Pending ? r.RequestedAt : r.DecidedAt ?? r.RequestedAt)
            .Select(Map)
            .ToList();
    }

    /// <summary>
    /// Autoriza o pedido. Não emite nada aqui: o token é entregue no GET do aparelho.
    /// Prazo do acesso = min(QR.ExpiresAt, agora + 24h).
    /// </summary>
    public async Task<DealerRequestResponse> ApproveAsync(
        Guid tournamentId, Guid requestId, Guid? userId, CancellationToken ct = default)
    {
        var request = await FindAsync(tournamentId, requestId, ct);
        if (request.Status == Approved)
            return Map(request); // toque duplo no botão
        if (request.Status != Pending)
            throw new DomainException(AlreadyDecidedMessage(request.Status));

        var now = DateTimeOffset.UtcNow;
        if (request.RequestedAt + PendingLifetime <= now)
        {
            request.Status = Rejected;
            request.DecidedAt = now;
            await _db.SaveChangesAsync(ct);
            throw new DomainException("Este pedido venceu (mais de 15 minutos sem resposta). Peça ao dealer para ler o QR de novo.");
        }

        var access = await _db.DealerAccesses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.DealerAccessId, ct);
        if (access is null || access.RevokedAt is not null)
            throw new DomainException("O QR usado neste pedido foi revogado. Peça ao dealer para ler o QR atual.");
        if (access.ExpiresAt <= now)
            throw new DomainException("O QR usado neste pedido venceu. Gere um QR novo e peça ao dealer para ler de novo.");

        var status = await _db.Tournaments
            .Where(t => t.Id == tournamentId)
            .Select(t => t.Status)
            .FirstOrDefaultAsync(ct);
        if (status is null || IsOver(status))
            throw new DomainException("O torneio já terminou.");

        var cap = now + MaxLifetime;
        request.Status = Approved;
        request.DecidedAt = now;
        request.DecidedBy = userId;
        request.ExpiresAt = access.ExpiresAt < cap ? access.ExpiresAt : cap;
        await _db.SaveChangesAsync(ct);
        _cache.Remove(CacheKey(request.Id));

        return Map(request);
    }

    public async Task<DealerRequestResponse> RejectAsync(
        Guid tournamentId, Guid requestId, Guid? userId, CancellationToken ct = default)
    {
        var request = await FindAsync(tournamentId, requestId, ct);
        if (request.Status == Rejected)
            return Map(request);
        if (request.Status != Pending)
            throw new DomainException(request.Status == Approved
                ? "Este pedido já foi autorizado. Para tirar o acesso, use Desconectar."
                : AlreadyDecidedMessage(request.Status));

        request.Status = Rejected;
        request.DecidedAt = DateTimeOffset.UtcNow;
        request.DecidedBy = userId;
        await _db.SaveChangesAsync(ct);
        return Map(request);
    }

    /// <summary>Desconecta um dealer aprovado: só aquele aparelho cai (em até 30s).</summary>
    public async Task<DealerRequestResponse> RevokeRequestAsync(
        Guid tournamentId, Guid requestId, Guid? userId, CancellationToken ct = default)
    {
        var request = await FindAsync(tournamentId, requestId, ct);
        if (request.Status == Revoked)
            return Map(request);
        if (request.Status == Pending)
            throw new DomainException("Este pedido ainda não foi autorizado: recuse-o em vez de desconectar.");
        if (request.Status == Rejected)
            throw new DomainException("Este pedido foi recusado; não há acesso para encerrar.");

        request.Status = Revoked;
        request.DecidedAt = DateTimeOffset.UtcNow;
        request.DecidedBy = userId;
        await _db.SaveChangesAsync(ct);
        MarkRevoked([request.Id]);
        return Map(request);
    }

    // -------------------------------------------------------- validação do token

    /// <summary>
    /// O pedido (sub do token) ainda vale? Approved, no prazo, QR de origem não revogado
    /// e torneio não terminado. Cache de 30s por id; marca o último acesso do terminal.
    /// </summary>
    public async Task<bool> IsActiveAsync(Guid requestId, CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(CacheKey(requestId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ValidityCacheTtl;
            var now = DateTimeOffset.UtcNow;
            var request = await _db.DealerRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct);
            if (request is null || request.Status != Approved || request.ExpiresAt is null || request.ExpiresAt <= now)
                return false;
            if (!await _db.DealerAccesses.AnyAsync(a => a.Id == request.DealerAccessId && a.RevokedAt == null, ct))
                return false;
            var status = await _db.Tournaments
                .Where(t => t.Id == request.TournamentId)
                .Select(t => t.Status)
                .FirstOrDefaultAsync(ct);
            if (status is null || IsOver(status))
                return false;

            // "Último acesso" na lista do organizador (no máximo uma gravação a cada 30s)
            await _db.DealerRequests
                .Where(r => r.Id == requestId)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.LastSeenAt, now), ct);
            return true;
        });
    }

    public async Task<DealerSessionResponse?> GetSessionAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _db.DealerRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
            return null;
        var name = await _db.Tournaments
            .Where(t => t.Id == request.TournamentId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);
        return name is null ? null : new DealerSessionResponse
        {
            RequestId = request.Id,
            TournamentId = request.TournamentId,
            TournamentName = name,
            DealerName = request.Name,
            ExpiresAt = request.ExpiresAt ?? request.RequestedAt,
        };
    }

    // ------------------------------------------------------------------ apoio

    // Torneio inexistente ou excluído (o filtro de soft delete o esconde) → 404. Quem não
    // é dono/membro nem chega aqui: o TenantAccessFilter resolve o home game também do
    // torneio excluído e responde 403.
    private async Task EnsureTournamentAsync(Guid tournamentId, CancellationToken ct)
    {
        if (!await _db.Tournaments.AnyAsync(t => t.Id == tournamentId, ct))
            throw new KeyNotFoundException();
    }

    // Approve/Reject/RevokeRequest: o pedido tem de ser deste torneio, e o torneio tem de existir
    private async Task<DealerRequest> FindAsync(Guid tournamentId, Guid requestId, CancellationToken ct)
    {
        await EnsureTournamentAsync(tournamentId, ct);
        return await _db.DealerRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.TournamentId == tournamentId, ct)
            ?? throw new KeyNotFoundException();
    }

    // Violação de unicidade (PostgreSQL 23505) sem depender do Npgsql aqui:
    // PostgresException é um DbException e expõe o SqlState
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is DbException { SqlState: "23505" };

    // Pendentes há mais de 15 min viram Rejected (sem DecidedBy = venceu sozinho)
    private async Task ExpireStalePendingAsync(Guid tournamentId, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - PendingLifetime;
        await _db.DealerRequests
            .Where(r => r.TournamentId == tournamentId && r.Status == Pending && r.RequestedAt <= cutoff)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, Rejected)
                .SetProperty(r => r.DecidedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);
    }

    // Recusados/vencidos com mais de 7 dias somem (mesmo padrão da limpeza de display_sessions).
    // Aprovados e revogados ficam: o histórico do torneio resolve "Dealer · Nome" por eles.
    private async Task CleanupOldRequestsAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - CleanupAge;
        await _db.DealerRequests
            .Where(r => (r.Status == Rejected || r.Status == Pending) && (r.DecidedAt ?? r.RequestedAt) < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    // Depois do commit: grava "inativo" no cache. Limpar antes do SaveChanges deixava
    // uma requisição concorrente ler o pedido ainda ativo e cachear "ativo" por 30s.
    private void MarkRevoked(IEnumerable<Guid> requestIds)
    {
        foreach (var id in requestIds)
            _cache.Set(CacheKey(id), false, ValidityCacheTtl);
    }

    /// <summary>
    /// Nome do dealer como aparece no painel do organizador e no histórico ("Dealer · Nome").
    /// Não aceita o que não se vê ou não se imprime: controle (NUL, que o PostgreSQL recusa
    /// e dava 500), formatação (zero-width, bidi override — nome vazio na tela ou texto
    /// invertido para se passar por outro), surrogate solto, uso privado e não atribuído →
    /// 400. O formulário (DealerAccessPage) REMOVE esses caracteres antes de enviar (colar
    /// um nome com zero-width não trava ninguém); aqui, o que chega com eles veio de fora da
    /// tela e é recusado — assim nada disso é gravado, nem "limpo" para outro nome.
    /// Espaços de qualquer tipo (inclusive tab/quebra de linha) viram um só; Unicode em
    /// NFC. Precisa de 2 a 40 caracteres com pelo menos 2 letras/dígitos.
    /// </summary>
    private static string NormalizeName(string? name)
    {
        var invalidMessage = $"Informe seu nome (de {NameMinLength} a {NameMaxLength} letras).";
        var source = (name ?? string.Empty).AsSpan();
        var kept = new StringBuilder(source.Length);
        while (!source.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(source, out var rune, out var consumed);
            if (status != OperationStatus.Done || consumed <= 0)
                throw new DomainException(invalidMessage); // surrogate solto
            var chunk = source[..consumed];
            source = source[consumed..];
            if (Rune.IsWhiteSpace(rune))
            {
                kept.Append(' ');
                continue;
            }
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                    throw new DomainException(invalidMessage);
            }
            kept.Append(chunk);
        }

        var normalized = kept.ToString().Normalize(NormalizationForm.FormC);
        normalized = string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var lettersOrDigits = 0;
        foreach (var rune in normalized.EnumerateRunes())
            if (Rune.IsLetterOrDigit(rune))
                lettersOrDigits++;

        if (normalized.Length < NameMinLength || normalized.Length > NameMaxLength || lettersOrDigits < NameMinLength)
            throw new DomainException(invalidMessage);
        return normalized;
    }

    private static bool IsCodeFormat(string code)
    {
        foreach (var c in code)
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
                return false;
        return true;
    }

    private static string AlreadyDecidedMessage(string status) => status == Revoked
        ? "Este acesso já foi encerrado. Peça ao dealer para ler o QR de novo."
        : "Este pedido já foi recusado. Peça ao dealer para ler o QR de novo.";

    private static bool IsOver(string status) =>
        status == nameof(TournamentStatus.Finished) || status == nameof(TournamentStatus.Cancelled);

    private static string CacheKey(Guid requestId) => $"dealer-request:{requestId}";

    // 128 bits aleatórios em base64url (22 caracteres): impossível de adivinhar
    private static string NewCode() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= 300 ? value : value[..300];

    private async Task<DealerAccessResponse> MapAccessAsync(DealerAccess access, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var pendingSince = now - PendingLifetime;
        var pending = await _db.DealerRequests.CountAsync(
            r => r.TournamentId == access.TournamentId && r.Status == Pending && r.RequestedAt > pendingSince, ct);
        var approved = await _db.DealerRequests.CountAsync(
            r => r.TournamentId == access.TournamentId && r.Status == Approved && r.ExpiresAt > now
                 && _db.DealerAccesses.Any(a => a.Id == r.DealerAccessId && a.RevokedAt == null), ct);
        return new DealerAccessResponse
        {
            Id = access.Id,
            Code = access.Code,
            ExpiresAt = access.ExpiresAt,
            CreatedAt = access.CreatedAt,
            PendingCount = pending,
            ApprovedCount = approved,
        };
    }

    private static DealerRequestCreatedResponse Created(DealerRequest request, string tournamentName) => new()
    {
        RequestId = request.Id,
        Status = request.Status,
        TournamentName = tournamentName,
    };

    private static DealerRequestResponse Map(DealerRequest request) => new()
    {
        Id = request.Id,
        Name = request.Name,
        Status = request.Status,
        RequestedAt = request.RequestedAt,
        DecidedAt = request.DecidedAt,
        ExpiresAt = request.ExpiresAt,
        LastSeenAt = request.LastSeenAt,
        UserAgent = request.UserAgent,
    };
}
