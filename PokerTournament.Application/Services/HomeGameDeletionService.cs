using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Excluir home game (SPEC-20261026-1126). Quem pode (dono ou Admin do sistema) o controller confere antes.
///
/// Exclusão LÓGICA em cascata, numa transação só: home game (DeletedAt + IsActive = false), torneios, jogadores
/// (Person) e rankings ganham DeletedAt — o filtro global tira tudo de todas as telas e APIs, inclusive do ranking
/// público (/r/{slug} → 404) e dos leaderboards; as estruturas de blinds do home game ficam IsActive = false (a lista
/// global do Admin não mostra estrutura de clube que não existe mais); os membros saem (delete físico de
/// home_game_members, como "Remover membro"); as TVs pareadas com torneios dele voltam para o QR; os QRs de dealer
/// são revogados e os dealers conectados caem. Grava AuditLog { EntityType = "HomeGame", Action = "HomeGameDeleted" }
/// com quem excluiu e as contagens. Nada é apagado de verdade: restaurar continua possível só no banco.
///
/// Recusa (400) com torneio em andamento — InProgress ou BreakSettlement ("Rebuys Finalizados", ainda é o jogo
/// rolando): "Encerre ou cancele o torneio X antes de excluir o home game." — e com o nome digitado diferente do nome
/// do home game (sem diferenciar maiúsculas e ignorando espaços nas pontas).
///
/// As escritas em lote são UPDATE direto (ExecuteUpdate): Remove() de entidades rastreadas dispararia a cascata do
/// change tracker (ex.: zerar o RankingId dos torneios) e o xmin das TVs/pedidos de dealer, que gravam "visto por
/// último" o tempo todo, faria a exclusão falhar à toa. UpdatedAt/DeletedAt vão à mão por isso (o override do
/// SaveChanges não vê UPDATE direto). O torneio que começar no meio da exclusão é pego pela conferência depois do
/// UPDATE e desfaz tudo (rollback).
///
/// SPEC-20261029-1723: o home game principal da conta nunca é excluído por aqui (400 "O home game principal da conta não
/// pode ser excluído.", também para o Admin do sistema); ele só sai junto com a conta (<see cref="DeleteWithAccountAsync"/>,
/// chamado pelo DELETE me), com a mesma cascata.
/// </summary>
public class HomeGameDeletionService
{
    public const string AuditEntityType = "HomeGame";
    public const string AuditAction = "HomeGameDeleted";
    public const string ConfirmNameRequiredMessage = "Digite o nome do home game para confirmar a exclusão.";
    public const string ConfirmNameMismatchMessage =
        "O nome digitado não é o nome deste home game. Confira e digite de novo para excluir.";

    private static readonly string[] RunningStatuses =
    [
        nameof(TournamentStatus.InProgress),
        nameof(TournamentStatus.BreakSettlement),
    ];

    private static readonly string Paired = nameof(DisplaySessionStatus.Paired);
    private static readonly string Pending = nameof(DisplaySessionStatus.Pending);

    private readonly IAppDbContext _db;
    private readonly DealerAccessService _dealers;
    private readonly IMemoryCache _cache;

    public HomeGameDeletionService(IAppDbContext db, DealerAccessService dealers, IMemoryCache cache)
    {
        _db = db;
        _dealers = dealers;
        _cache = cache;
    }

    /// <summary>Mensagem da recusa com torneio em andamento.</summary>
    public static string RunningTournamentMessage(string tournamentName) =>
        $"Encerre ou cancele o torneio {tournamentName} antes de excluir o home game.";

    /// <summary>O nome digitado confere? Sem diferenciar maiúsculas, ignorando espaços nas pontas (Unicode em NFC).</summary>
    public static bool NameMatches(string? typed, string name) =>
        !string.IsNullOrWhiteSpace(typed)
        && string.Equals(Canonical(typed), Canonical(name), StringComparison.OrdinalIgnoreCase);

    private static string Canonical(string value) => value.Trim().Normalize(NormalizationForm.FormC);

    /// <summary>
    /// O que some junto. Null = home game inexistente ou já excluído. O principal da conta (SPEC-20261029-1723) vem com
    /// <c>isPrimary</c> e o <c>blockedMessage</c> "O home game principal da conta não pode ser excluído." (antes do
    /// torneio em andamento, que continua em <c>blockingTournament*</c>).
    /// </summary>
    public async Task<HomeGameDeletionSummaryResponse?> GetSummaryAsync(Guid homeGameId, CancellationToken ct = default)
    {
        var homeGame = await _db.HomeGames.AsNoTracking()
            .Where(h => h.Id == homeGameId)
            .Select(h => new { h.Id, h.Name, h.OwnerId, h.IsPrimary, OwnerName = h.Owner.FullName })
            .FirstOrDefaultAsync(ct);
        if (homeGame is null)
            return null;

        var running = await RunningTournamentAsync(homeGameId, ct);
        return new HomeGameDeletionSummaryResponse
        {
            HomeGameId = homeGame.Id,
            Name = homeGame.Name,
            OwnerId = homeGame.OwnerId,
            OwnerName = homeGame.OwnerName,
            IsPrimary = homeGame.IsPrimary,
            Tournaments = await _db.Tournaments.CountAsync(t => t.HomeGameId == homeGameId, ct),
            Players = await _db.Persons.CountAsync(p => p.HomeGameId == homeGameId, ct),
            Rankings = await _db.Rankings.CountAsync(r => r.HomeGameId == homeGameId, ct),
            Members = await _db.HomeGameMembers.CountAsync(m => m.HomeGameId == homeGameId, ct),
            BlockingTournamentId = running?.Id,
            BlockingTournamentName = running?.Name,
            BlockedMessage = homeGame.IsPrimary
                ? HomeGamePrimary.NotDeletableMessage
                : running is null ? null : RunningTournamentMessage(running.Name),
        };
    }

    /// <summary>
    /// Exclui o home game em cascata (ver a classe). Null = home game inexistente ou já excluído (404).
    /// Principal da conta (SPEC-20261029-1723, também para o Admin do sistema), nome vazio/diferente ou torneio em
    /// andamento → <see cref="DomainException"/> (400) sem mudar nada.
    /// </summary>
    public async Task<HomeGameDeletionSummaryResponse?> DeleteAsync(
        Guid homeGameId, string? confirmName, Guid byUserId, bool bySystemAdmin, string? ip, string? userAgent,
        CancellationToken ct = default)
    {
        var summary = await GetSummaryAsync(homeGameId, ct);
        if (summary is null)
            return null;

        if (summary.BlockedMessage is not null)
            throw new DomainException(summary.BlockedMessage);
        if (string.IsNullOrWhiteSpace(confirmName))
            throw new DomainException(ConfirmNameRequiredMessage);
        if (!NameMatches(confirmName, summary.Name))
            throw new DomainException(ConfirmNameMismatchMessage);

        HomeGameDeletionCleanup? cleanup;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            cleanup = await CascadeAsync(summary, byUserId, bySystemAdmin, withAccount: false, ip, userAgent,
                RunningTournamentMessage, ct);
            if (cleanup is null)
                return null; // outra requisição excluiu no meio do caminho
            await tx.CommitAsync(ct);
        }

        AfterCommit(cleanup);
        return summary;
    }

    // ------------------------------------------------------------------ junto com a conta (SPEC-20261029-1723)

    /// <summary>Mensagem do DELETE me quando o principal da conta tem torneio em andamento.</summary>
    public static string AccountRunningTournamentMessage(string tournamentName) =>
        $"Encerre ou cancele o torneio {tournamentName} antes de excluir a conta.";

    /// <summary>Resumo do principal da conta (o que sai junto no DELETE me), ou null se ela não tem principal.</summary>
    public async Task<HomeGameDeletionSummaryResponse?> GetAccountPrimarySummaryAsync(Guid ownerId, CancellationToken ct = default)
    {
        var id = await HomeGamePrimary.FindIdAsync(_db, ownerId, ct);
        return id is null ? null : await GetSummaryAsync(id.Value, ct);
    }

    /// <summary>
    /// Excluir a conta (<c>DELETE me</c>) leva junto o principal dela — que pela tela do home game nunca é excluído — com
    /// a MESMA cascata (torneios, jogadores e rankings com <c>deleted_at</c>, membros fora, TVs e dealers desconectados,
    /// AuditLog <c>HomeGameDeleted</c> com <c>withAccount: true</c>). Roda DENTRO da transação de quem chama (exigida);
    /// depois do commit, chamar <see cref="AfterCommit"/> com o que voltar. Torneio que começou no meio → 400
    /// "Encerre ou cancele o torneio X antes de excluir a conta." e quem chama desfaz tudo. Null = o principal já não
    /// existia.
    /// </summary>
    public async Task<HomeGameDeletionCleanup?> DeleteWithAccountAsync(
        HomeGameDeletionSummaryResponse primary, Guid ownerId, string? ip, string? userAgent, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Excluir o principal junto com a conta exige a transação da exclusão da conta.");
        return await CascadeAsync(primary, ownerId, bySystemAdmin: false, withAccount: true, ip, userAgent,
            AccountRunningTournamentMessage, ct);
    }

    /// <summary>
    /// Depois do commit: o terminal do dealer cai na próxima requisição e o link público do ranking dá 404 já.
    /// </summary>
    public void AfterCommit(HomeGameDeletionCleanup cleanup)
    {
        _dealers.ForgetRevokedRequests(cleanup.DroppedDealerRequests);
        foreach (var slug in cleanup.RankingSlugs)
            _cache.Remove(RankingLeaderboardService.PublicCacheKey(slug));
    }

    // ------------------------------------------------------------------ cascata

    /// <summary>
    /// A cascata em si, dentro da transação aberta por quem chama (sem commit). Null = o home game já tinha sido
    /// excluído. Preenche as contagens de <paramref name="summary"/> com o que foi excluído.
    /// </summary>
    private async Task<HomeGameDeletionCleanup?> CascadeAsync(
        HomeGameDeletionSummaryResponse summary, Guid byUserId, bool bySystemAdmin, bool withAccount,
        string? ip, string? userAgent, Func<string, string> runningMessage, CancellationToken ct)
    {
        var homeGameId = summary.HomeGameId;
        var now = DateTimeOffset.UtcNow;
        var running = RunningStatuses;

        // Home game primeiro: segura a linha (UPDATE) até o commit — dois cliques não excluem duas vezes
        var homeGames = await _db.HomeGames
            .Where(h => h.Id == homeGameId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(h => h.DeletedAt, now)
                .SetProperty(h => h.IsActive, false)
                .SetProperty(h => h.UpdatedAt, now), ct);
        if (homeGames == 0)
            return null;

        // Torneios do home game, inclusive os já excluídos antes: TV e QR de dealer deles também caem
        var allTournamentIds = await _db.Tournaments.IgnoreQueryFilters()
            .Where(t => t.HomeGameId == homeGameId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var tournaments = await _db.Tournaments
            .Where(t => t.HomeGameId == homeGameId && !running.Contains(t.Status))
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.DeletedAt, now)
                .SetProperty(t => t.UpdatedAt, now), ct);

        // Torneio que começou depois da conferência lá em cima: o UPDATE pulou ele (status) — desfaz tudo
        var startedMeanwhile = await _db.Tournaments.AsNoTracking()
            .Where(t => t.HomeGameId == homeGameId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);
        if (startedMeanwhile is not null)
            throw new DomainException(runningMessage(startedMeanwhile));

        var rankingSlugs = await _db.Rankings
            .Where(r => r.HomeGameId == homeGameId && r.Slug != null)
            .Select(r => r.Slug!)
            .ToListAsync(ct);
        var rankings = await _db.Rankings
            .Where(r => r.HomeGameId == homeGameId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.DeletedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);

        // Membros antes dos jogadores (o membro pode estar vinculado a um jogador)
        var members = await _db.HomeGameMembers
            .Where(m => m.HomeGameId == homeGameId)
            .ExecuteDeleteAsync(ct);

        var players = await _db.Persons
            .Where(p => p.HomeGameId == homeGameId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.DeletedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);

        var structures = await _db.BlindStructures
            .Where(b => b.HomeGameId == homeGameId && b.IsActive)
            .ExecuteUpdateAsync(set => set
                .SetProperty(b => b.IsActive, false)
                .SetProperty(b => b.UpdatedAt, now), ct);

        // TVs mostrando um torneio do home game voltam para o QR grande (como o "Voltar" do telão)
        var displays = allTournamentIds.Count == 0 ? 0 : await _db.DisplaySessions
            .Where(s => s.Status == Paired && s.TournamentId != null && allTournamentIds.Contains(s.TournamentId.Value))
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Status, Pending)
                .SetProperty(s => s.TournamentId, (Guid?)null)
                .SetProperty(s => s.PairedAt, (DateTimeOffset?)null)
                .SetProperty(s => s.UpdatedAt, now), ct);

        var dealer = await _dealers.RevokeForTournamentsAsync(allTournamentIds, byUserId, ct);

        var byName = await _db.Users.AsNoTracking()
            .Where(u => u.Id == byUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct);

        _db.AuditLogs.Add(new AuditLog
        {
            // AuditLog não é BaseEntity: Id e CreatedAt à mão
            Id = Guid.NewGuid(),
            EntityType = AuditEntityType,
            EntityId = homeGameId,
            Action = AuditAction,
            OldValues = JsonSerializer.Serialize(new
            {
                name = summary.Name,
                ownerId = summary.OwnerId,
                ownerName = summary.OwnerName,
                isPrimary = summary.IsPrimary,
            }),
            NewValues = JsonSerializer.Serialize(new
            {
                tournaments,
                players,
                rankings,
                members,
                blindStructures = structures,
                tvsUnpaired = displays,
                dealerQrsRevoked = dealer.Accesses,
                dealerRequestsClosed = dealer.Requests,
                bySystemAdmin,
                withAccount,
            }),
            UserId = byUserId == Guid.Empty ? null : byUserId,
            UserName = byName,
            IpAddress = Cut(ip, 50),
            UserAgent = Cut(userAgent, 500),
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(ct);

        summary.Tournaments = tournaments;
        summary.Players = players;
        summary.Rankings = rankings;
        summary.Members = members;
        return new HomeGameDeletionCleanup(dealer.DroppedRequestIds, rankingSlugs);
    }

    // Em andamento primeiro o mais recente (é o que aparece na mensagem)
    private async Task<Tournament?> RunningTournamentAsync(Guid homeGameId, CancellationToken ct)
    {
        var running = RunningStatuses;
        return await _db.Tournaments.AsNoTracking()
            .Where(t => t.HomeGameId == homeGameId && running.Contains(t.Status))
            .OrderByDescending(t => t.Date)
            .ThenBy(t => t.Name)
            .FirstOrDefaultAsync(ct);
    }

    private static string? Cut(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}

/// <summary>O que fica para depois do commit de uma exclusão (cache do dealer e do ranking público).</summary>
public sealed record HomeGameDeletionCleanup(List<Guid> DroppedDealerRequests, List<string> RankingSlugs);
