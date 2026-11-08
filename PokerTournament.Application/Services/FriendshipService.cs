using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Amigos, pedidos e bloqueios (SPEC-20261108-1637, plano §4 e D11). É também a implementação real de
/// <see cref="IFriendshipLookup"/> (a costura que a SPEC-20261108-1636 deixou nula): o perfil <c>Friends</c>, o 404 do
/// bloqueio e o "jogou com você" sem bloqueados passam a valer sem mexer em quem já usa a interface; a SPEC de mensagens
/// (1638) consulta só <see cref="IsBlockedAsync"/>.
/// <para>
/// <b>Um registro por par</b> (<see cref="Friendship"/>, par ordenado com índice único). Pedir: recusado com mensagem
/// clara quando é a própria conta, já são amigos, já há pedido meu pendente, quem recebe não aceita pedidos
/// (<c>FriendRequestsFrom</c> = Nobody) ou só aceita de quem jogou com ele (PlayedWith sem torneio em comum —
/// inscrições vinculadas no mesmo torneio não cancelado, ignorando <c>ShowInPlayedWith</c>), recusa há menos de 30 dias
/// (a mensagem diz a data e NÃO diz "recusado") ou mais de 20 pedidos pendentes enviados. Bloqueio em qualquer direção
/// responde como se o perfil não existisse (null → 404). Se o OUTRO já tinha pedido, o pedido cruzado vira amizade.
/// </para>
/// <para>
/// <b>Responder</b> só por quem recebeu; a recusa não avisa quem pediu e o pedido some para ele. <b>Bloquear</b>
/// converte o registro do par em <c>Blocked</c> (apaga amizade/pedido) e só quem bloqueou desbloqueia; quem foi
/// bloqueado não é avisado. <b>Notificações</b> (categoria Social): pedido recebido e pedido aceito, preparadas no
/// mesmo <c>SaveChangesAsync</c> do fluxo e empurradas depois.
/// </para>
/// </summary>
public class FriendshipService : IFriendshipLookup
{
    public const int MaxPendingSent = 20;
    public const int DeclineCooldownDays = 30;
    public const int MaxMutualFriends = 6;

    public const string SelfRequestMessage = "Você não pode pedir amizade a si mesmo.";
    public const string AlreadyFriendsMessage = "Vocês já são amigos.";
    public const string AlreadyPendingMessage = "Você já pediu amizade a este jogador. Aguarde a resposta.";
    public const string RecipientAcceptsNobodyMessage = "Este jogador não recebe pedidos de amizade.";
    public const string RecipientAcceptsPlayedWithMessage = "Este jogador só aceita pedidos de amizade de quem já jogou um torneio com ele.";
    public const string TooManyPendingMessage = "Você já tem 20 pedidos de amizade aguardando resposta. Cancele algum ou espere uma resposta para pedir de novo.";
    public const string RequestNotFoundMessage = "Pedido de amizade não encontrado.";
    public const string FriendNotFoundMessage = "Vocês não são amigos.";
    public const string NotBlockedMessage = "Este jogador não está bloqueado por você.";
    public const string RaceMessage = "Já existe um pedido entre vocês. Recarregue a página.";

    /// <summary>Caminho da aba Pedidos da lista de amigos (link da notificação de pedido recebido).</summary>
    public const string RequestsLink = "/amigos?aba=pedidos";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    private static readonly string[] PlayingEntryStatuses =
    [
        nameof(EntryStatus.Registered),
        nameof(EntryStatus.Active),
    ];

    private readonly IAppDbContext _db;
    private readonly NotificationService _notifications;
    private readonly PlanService _plans;

    public FriendshipService(IAppDbContext db, NotificationService notifications, PlanService plans)
    {
        _db = db;
        _notifications = notifications;
        _plans = plans;
    }

    // ------------------------------------------------------------------ IFriendshipLookup

    public Task<bool> AreFriendsAsync(Guid userA, Guid userB, CancellationToken ct = default)
    {
        if (userA == userB)
            return Task.FromResult(false);
        var (low, high) = Friendship.Order(userA, userB);
        var accepted = nameof(FriendshipStatus.Accepted);
        return _db.Friendships.AnyAsync(f => f.UserLowId == low && f.UserHighId == high && f.Status == accepted, ct);
    }

    public Task<bool> IsBlockedAsync(Guid userA, Guid userB, CancellationToken ct = default)
    {
        if (userA == userB)
            return Task.FromResult(false);
        var (low, high) = Friendship.Order(userA, userB);
        var blocked = nameof(FriendshipStatus.Blocked);
        return _db.Friendships.AnyAsync(f => f.UserLowId == low && f.UserHighId == high && f.Status == blocked, ct);
    }

    // ------------------------------------------------------------------ relação (perfil e cartões)

    /// <summary>
    /// A relação de <paramref name="viewerId"/> com <paramref name="targetId"/>, com "pode pedir" pela privacidade de
    /// quem recebe. Null quando há bloqueio em qualquer direção (quem chama responde 404).
    /// </summary>
    public async Task<ProfileFriendshipResponse?> GetRelationAsync(Guid viewerId, Guid targetId, CancellationToken ct = default)
    {
        if (viewerId == targetId)
            return new ProfileFriendshipResponse { Status = FriendshipState.Self };

        var pair = await FindPairAsync(viewerId, targetId, ct);
        if (pair is not null && pair.Status == nameof(FriendshipStatus.Blocked))
            return null;

        var target = await _db.Users.AsNoTracking()
            .Where(u => u.Id == targetId)
            .Select(u => new { u.FriendRequestsFrom })
            .FirstOrDefaultAsync(ct);
        if (target is null)
            return null;

        return await RelationOfAsync(viewerId, targetId, pair, target.FriendRequestsFrom, ct);
    }

    /// <summary>
    /// Relação de quem vê com várias contas de uma vez (cartões do "jogou com você"): um dicionário por id. Contas
    /// bloqueadas em qualquer direção ficam de fora do resultado.
    /// </summary>
    public async Task<Dictionary<Guid, ProfileFriendshipResponse>> GetRelationsAsync(
        Guid viewerId, IReadOnlyCollection<Guid> targetIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, ProfileFriendshipResponse>();
        if (targetIds.Count == 0)
            return result;

        var ids = targetIds.Where(id => id != viewerId).Distinct().ToList();
        var pairs = await _db.Friendships.AsNoTracking()
            .Where(f => (f.UserLowId == viewerId && ids.Contains(f.UserHighId))
                || (f.UserHighId == viewerId && ids.Contains(f.UserLowId)))
            .ToListAsync(ct);
        var byOther = pairs.ToDictionary(p => p.OtherOf(viewerId));
        var prefs = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FriendRequestsFrom })
            .ToDictionaryAsync(u => u.Id, u => u.FriendRequestsFrom, ct);

        foreach (var id in ids)
        {
            byOther.TryGetValue(id, out var pair);
            if (pair is not null && pair.Status == nameof(FriendshipStatus.Blocked))
                continue;
            if (!prefs.TryGetValue(id, out var pref))
                continue;
            result[id] = await RelationOfAsync(viewerId, id, pair, pref, ct);
        }
        return result;
    }

    private async Task<ProfileFriendshipResponse> RelationOfAsync(
        Guid viewerId, Guid targetId, Friendship? pair, string recipientPreference, CancellationToken ct)
    {
        var relation = new ProfileFriendshipResponse();
        switch (pair?.Status)
        {
            case nameof(FriendshipStatus.Accepted):
                relation.Status = FriendshipState.Friends;
                return relation;
            case nameof(FriendshipStatus.Pending):
                relation.Status = pair.RequesterId == viewerId ? FriendshipState.RequestSent : FriendshipState.RequestReceived;
                relation.FriendshipId = pair.Id;
                return relation;
        }

        relation.Status = FriendshipState.None;
        var reason = await CannotRequestReasonAsync(viewerId, targetId, recipientPreference, ct);
        relation.CanRequest = reason is null;
        relation.CannotRequestReason = reason;
        return relation;
    }

    /// <summary>Null quando a privacidade de quem recebe deixa pedir; senão a mensagem do motivo.</summary>
    private async Task<string?> CannotRequestReasonAsync(Guid requesterId, Guid recipientId, string preference, CancellationToken ct)
    {
        if (preference == nameof(FriendRequestsFrom.Nobody))
            return RecipientAcceptsNobodyMessage;
        if (preference == nameof(FriendRequestsFrom.PlayedWith) && !await HavePlayedTogetherAsync(requesterId, recipientId, ct))
            return RecipientAcceptsPlayedWithMessage;
        return null;
    }

    /// <summary>
    /// As duas contas têm inscrição vinculada no MESMO torneio (não cancelado, não excluído, em clube não excluído) —
    /// a relação do "jogou com você", sem olhar <c>ShowInPlayedWith</c>.
    /// </summary>
    public async Task<bool> HavePlayedTogetherAsync(Guid userA, Guid userB, CancellationToken ct = default)
    {
        var cancelled = nameof(TournamentStatus.Cancelled);
        var tournamentsOfA = _db.TournamentEntries.IgnoreQueryFilters()
            .Where(e => e.Person.UserId == userA
                && e.Tournament.DeletedAt == null
                && e.Tournament.Status != cancelled
                && e.Tournament.HomeGame.DeletedAt == null)
            .Select(e => e.TournamentId);
        return await _db.TournamentEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.Person.UserId == userB && tournamentsOfA.Contains(e.TournamentId), ct);
    }

    // ------------------------------------------------------------------ pedir

    /// <summary>
    /// <c>POST players/{handle}/friend-request</c>. Null = handle inexistente/inativo ou bloqueio em qualquer direção
    /// (404, como o perfil). <see cref="DomainException"/> (400) nas recusas com mensagem. Pedido cruzado → amizade.
    /// </summary>
    public async Task<FriendshipActionResponse?> RequestAsync(Guid requesterId, string handle, CancellationToken ct = default)
    {
        var target = await FindActiveByHandleAsync(handle, ct);
        if (target is null)
            return null;
        if (target.Id == requesterId)
            throw new DomainException(SelfRequestMessage);

        var now = DateTimeOffset.UtcNow;
        var pair = await FindPairAsync(requesterId, target.Id, ct, track: true);
        if (pair is not null)
        {
            switch (pair.Status)
            {
                case nameof(FriendshipStatus.Blocked):
                    return null;
                case nameof(FriendshipStatus.Accepted):
                    throw new DomainException(AlreadyFriendsMessage);
                case nameof(FriendshipStatus.Pending) when pair.RequesterId == requesterId:
                    throw new DomainException(AlreadyPendingMessage);
                case nameof(FriendshipStatus.Pending):
                    // O outro já tinha pedido: aceitar vale mais que criar outro pedido
                    await AcceptPairAsync(pair, requesterId, now, ct);
                    return new FriendshipActionResponse
                    {
                        Friendship = new ProfileFriendshipResponse { Status = FriendshipState.Friends },
                        BecameFriends = true,
                    };
                case nameof(FriendshipStatus.Declined) when pair.RequesterId == requesterId
                    && pair.RespondedAt is { } declinedAt && declinedAt.AddDays(DeclineCooldownDays) > now:
                    throw new DomainException(CooldownMessage(declinedAt.AddDays(DeclineCooldownDays)));
            }
        }

        var reason = await CannotRequestReasonAsync(requesterId, target.Id, target.FriendRequestsFrom, ct);
        if (reason is not null)
            throw new DomainException(reason);

        var pending = nameof(FriendshipStatus.Pending);
        var pendingSent = await _db.Friendships.CountAsync(f => f.RequesterId == requesterId && f.Status == pending, ct);
        if (pendingSent >= MaxPendingSent)
            throw new DomainException(TooManyPendingMessage);

        if (pair is null)
        {
            var (low, high) = Friendship.Order(requesterId, target.Id);
            pair = new Friendship { UserLowId = low, UserHighId = high };
            _db.Friendships.Add(pair);
        }
        // Linha Declined (minha, já fora da carência, ou do outro que eu recusei) vira o pedido novo
        pair.RequesterId = requesterId;
        pair.Status = pending;
        pair.RequestedAt = now;
        pair.RespondedAt = null;
        pair.BlockedById = null;

        var requesterName = await DisplayNameAsync(requesterId, ct);
        await _notifications.StageAsync(
            [target.Id],
            NotificationKind.FriendRequestReceived,
            "Pedido de amizade",
            $"{requesterName} quer ser seu amigo no Poker da Galera.",
            RequestsLink,
            ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            // Corrida: os dois pediram ao mesmo tempo e o outro gravou primeiro
            throw new DomainException(RaceMessage);
        }
        await _notifications.PushPendingAsync(ct);

        return new FriendshipActionResponse
        {
            Friendship = new ProfileFriendshipResponse { Status = FriendshipState.RequestSent, FriendshipId = pair.Id },
        };
    }

    private static string CooldownMessage(DateTimeOffset until)
    {
        var date = until.ToOffset(BrasiliaOffset).ToString("dd/MM/yyyy", PtBr);
        return $"Você já pediu amizade a este jogador recentemente. Pode pedir de novo a partir de {date}.";
    }

    // ------------------------------------------------------------------ responder, cancelar, desfazer

    /// <summary>Aceita um pedido pendente recebido por <paramref name="userId"/>. Null = não existe ou não é dele (404).</summary>
    public async Task<FriendshipActionResponse?> AcceptAsync(Guid userId, Guid friendshipId, CancellationToken ct = default)
    {
        var pair = await FindReceivedPendingAsync(userId, friendshipId, ct);
        if (pair is null)
            return null;
        await AcceptPairAsync(pair, userId, DateTimeOffset.UtcNow, ct);
        return new FriendshipActionResponse
        {
            Friendship = new ProfileFriendshipResponse { Status = FriendshipState.Friends },
            BecameFriends = true,
        };
    }

    /// <summary>Recusa um pedido pendente recebido. Quem pediu NÃO é avisado. Null = não existe ou não é dele (404).</summary>
    public async Task<bool> DeclineAsync(Guid userId, Guid friendshipId, CancellationToken ct = default)
    {
        var pair = await FindReceivedPendingAsync(userId, friendshipId, ct);
        if (pair is null)
            return false;
        pair.Status = nameof(FriendshipStatus.Declined);
        pair.RespondedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Quem pediu cancela o pedido pendente (apaga a linha). Falso = não existe ou não é dele (404).</summary>
    public async Task<bool> CancelRequestAsync(Guid userId, Guid friendshipId, CancellationToken ct = default)
    {
        var pending = nameof(FriendshipStatus.Pending);
        var pair = await _db.Friendships
            .FirstOrDefaultAsync(f => f.Id == friendshipId && f.Status == pending && f.RequesterId == userId, ct);
        if (pair is null)
            return false;
        _db.Friendships.Remove(pair);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Desfaz a amizade (apaga a linha). Falso = não são amigos (404).</summary>
    public async Task<bool> RemoveFriendAsync(Guid userId, Guid otherUserId, CancellationToken ct = default)
    {
        var pair = await FindPairAsync(userId, otherUserId, ct, track: true);
        if (pair is null || pair.Status != nameof(FriendshipStatus.Accepted))
            return false;
        _db.Friendships.Remove(pair);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task AcceptPairAsync(Friendship pair, Guid acceptorId, DateTimeOffset now, CancellationToken ct)
    {
        pair.Status = nameof(FriendshipStatus.Accepted);
        pair.RespondedAt = now;

        var acceptor = await _db.Users.AsNoTracking()
            .Where(u => u.Id == acceptorId)
            .Select(u => new { u.Handle, u.Nickname, u.FullName })
            .FirstAsync(ct);
        var requesterId = pair.OtherOf(acceptorId);
        await _notifications.StageAsync(
            [requesterId],
            NotificationKind.FriendRequestAccepted,
            "Pedido de amizade aceito",
            $"{PlayerProfileService.DisplayNameOf(acceptor.Nickname, acceptor.FullName)} aceitou o seu pedido. Vocês agora são amigos.",
            $"/jogador/{acceptor.Handle}",
            ct);
        await _db.SaveChangesAsync(ct);
        await _notifications.PushPendingAsync(ct);
    }

    private Task<Friendship?> FindReceivedPendingAsync(Guid userId, Guid friendshipId, CancellationToken ct)
    {
        var pending = nameof(FriendshipStatus.Pending);
        return _db.Friendships.FirstOrDefaultAsync(
            f => f.Id == friendshipId && f.Status == pending && f.RequesterId != userId
                && (f.UserLowId == userId || f.UserHighId == userId), ct);
    }

    // ------------------------------------------------------------------ bloquear

    /// <summary>
    /// <c>POST players/{handle}/block</c>: o registro do par vira Blocked (apagando amizade ou pedido). Idempotente para
    /// quem já bloqueou. Null = handle inexistente/inativo ou o OUTRO já bloqueou (404: para mim ele não existe).
    /// Quem foi bloqueado não é avisado.
    /// </summary>
    public async Task<bool?> BlockAsync(Guid userId, string handle, CancellationToken ct = default)
    {
        var target = await FindActiveByHandleAsync(handle, ct);
        if (target is null)
            return null;
        if (target.Id == userId)
            throw new DomainException("Você não pode bloquear a si mesmo.");

        var pair = await FindPairAsync(userId, target.Id, ct, track: true);
        if (pair is not null && pair.Status == nameof(FriendshipStatus.Blocked))
            return pair.BlockedById == userId ? true : null;

        if (pair is null)
        {
            var (low, high) = Friendship.Order(userId, target.Id);
            pair = new Friendship { UserLowId = low, UserHighId = high };
            _db.Friendships.Add(pair);
        }
        pair.RequesterId = userId;
        pair.BlockedById = userId;
        pair.Status = nameof(FriendshipStatus.Blocked);
        pair.RequestedAt = DateTimeOffset.UtcNow;
        pair.RespondedAt = null;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            throw new DomainException(RaceMessage);
        }
        return true;
    }

    /// <summary>
    /// <c>DELETE players/{handle}/block</c>: só quem bloqueou desbloqueia (apaga a linha: voltam a ser desconhecidos).
    /// Null = handle inexistente; <see cref="DomainException"/> quando não há bloqueio meu sobre essa conta.
    /// </summary>
    public async Task<bool?> UnblockAsync(Guid userId, string handle, CancellationToken ct = default)
    {
        var key = NormalizeHandle(handle);
        var targetId = await _db.Users.AsNoTracking()
            .Where(u => u.Handle == key)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        if (targetId is null)
            return null;

        var pair = await FindPairAsync(userId, targetId.Value, ct, track: true);
        if (pair is null || pair.Status != nameof(FriendshipStatus.Blocked) || pair.BlockedById != userId)
            throw new DomainException(NotBlockedMessage);
        _db.Friendships.Remove(pair);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary><c>GET me/blocks</c>: as contas (ativas) que eu bloqueei, da mais recente.</summary>
    public async Task<List<BlockedUserResponse>> ListBlocksAsync(Guid userId, CancellationToken ct = default)
    {
        var blocked = nameof(FriendshipStatus.Blocked);
        var rows = await _db.Friendships.AsNoTracking()
            .Where(f => f.Status == blocked && f.BlockedById == userId)
            .Select(f => new { f.RequestedAt, OtherId = f.UserLowId == userId ? f.UserHighId : f.UserLowId })
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];
        var users = await UsersAsync(rows.Select(r => r.OtherId).ToList(), activeOnly: true, ct);
        return rows
            .Where(r => users.ContainsKey(r.OtherId))
            .OrderByDescending(r => r.RequestedAt)
            .Select(r => Fill(new BlockedUserResponse { BlockedAt = r.RequestedAt }, users[r.OtherId]))
            .ToList();
    }

    /// <summary>Administração: bloqueios feitos pela conta e sofridos por ela (contas inativas incluídas), com datas.</summary>
    public async Task<AdminUserBlocksResponse> AdminBlocksAsync(Guid userId, CancellationToken ct = default)
    {
        var blocked = nameof(FriendshipStatus.Blocked);
        var rows = await _db.Friendships.AsNoTracking()
            .Where(f => f.Status == blocked && (f.UserLowId == userId || f.UserHighId == userId))
            .Select(f => new { f.RequestedAt, f.BlockedById, OtherId = f.UserLowId == userId ? f.UserHighId : f.UserLowId })
            .ToListAsync(ct);
        var response = new AdminUserBlocksResponse();
        if (rows.Count == 0)
            return response;
        var users = await _db.Users.AsNoTracking()
            .Where(u => rows.Select(r => r.OtherId).Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Handle, u.PhotoUrl, u.IsActive })
            .ToDictionaryAsync(u => u.Id, ct);
        foreach (var r in rows.OrderByDescending(r => r.RequestedAt))
        {
            if (!users.TryGetValue(r.OtherId, out var u))
                continue;
            var entry = new AdminBlockEntryResponse
            {
                UserId = u.Id,
                FullName = u.FullName,
                Handle = u.Handle,
                PhotoUrl = u.PhotoUrl,
                IsActive = u.IsActive,
                BlockedAt = r.RequestedAt,
            };
            (r.BlockedById == userId ? response.Made : response.Received).Add(entry);
        }
        return response;
    }

    // ------------------------------------------------------------------ listas

    /// <summary>
    /// <c>GET me/friends</c>: os amigos (contas ativas) por apelido, com "jogando agora" só nos torneios em andamento que
    /// quem vê pode acompanhar (inscrito → /meus-torneios/{id}; dono ou membro ativo do clube → /tournaments/{id}).
    /// </summary>
    public async Task<List<FriendResponse>> ListFriendsAsync(Guid userId, CancellationToken ct = default)
    {
        var accepted = nameof(FriendshipStatus.Accepted);
        var rows = await _db.Friendships.AsNoTracking()
            .Where(f => f.Status == accepted && (f.UserLowId == userId || f.UserHighId == userId))
            .Select(f => new { Since = f.RespondedAt ?? f.CreatedAt, OtherId = f.UserLowId == userId ? f.UserHighId : f.UserLowId })
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];

        var ids = rows.Select(r => r.OtherId).ToList();
        var users = await UsersAsync(ids, activeOnly: true, ct);
        var playing = await PlayingNowAsync(userId, ids, ct);

        return rows
            .Where(r => users.ContainsKey(r.OtherId))
            .Select(r =>
            {
                var friend = Fill(new FriendResponse { FriendsSince = r.Since }, users[r.OtherId]);
                playing.TryGetValue(r.OtherId, out var now);
                friend.PlayingNow = now;
                return friend;
            })
            .OrderByDescending(f => f.PlayingNow is not null)
            .ThenBy(f => f.DisplayName, StringComparer.Create(PtBr, ignoreCase: true))
            .ThenBy(f => f.Handle, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary><c>GET me/friend-requests</c>: pendentes recebidos e enviados (contas ativas), mais recentes primeiro.</summary>
    public async Task<FriendRequestsResponse> ListRequestsAsync(Guid userId, CancellationToken ct = default)
    {
        var pending = nameof(FriendshipStatus.Pending);
        var rows = await _db.Friendships.AsNoTracking()
            .Where(f => f.Status == pending && (f.UserLowId == userId || f.UserHighId == userId))
            .Select(f => new { f.Id, f.RequesterId, f.RequestedAt, OtherId = f.UserLowId == userId ? f.UserHighId : f.UserLowId })
            .ToListAsync(ct);
        var response = new FriendRequestsResponse();
        if (rows.Count == 0)
            return response;

        var users = await UsersAsync(rows.Select(r => r.OtherId).ToList(), activeOnly: true, ct);
        foreach (var r in rows.OrderByDescending(r => r.RequestedAt))
        {
            if (!users.TryGetValue(r.OtherId, out var u))
                continue;
            var item = new FriendRequestResponse { Id = r.Id, RequestedAt = r.RequestedAt, User = Fill(new FriendUserResponse(), u) };
            (r.RequesterId == userId ? response.Sent : response.Received).Add(item);
        }
        return response;
    }

    /// <summary>Quantos pedidos pendentes a conta recebeu (bloco do Início e aba Pedidos).</summary>
    public Task<int> CountReceivedPendingAsync(Guid userId, CancellationToken ct = default)
    {
        var pending = nameof(FriendshipStatus.Pending);
        return _db.Friendships.CountAsync(
            f => f.Status == pending && f.RequesterId != userId && (f.UserLowId == userId || f.UserHighId == userId), ct);
    }

    /// <summary>Quantos amigos (contas ativas) a conta tem.</summary>
    public async Task<int> CountFriendsAsync(Guid userId, CancellationToken ct = default)
    {
        var ids = await FriendIdsAsync(userId, ct);
        if (ids.Count == 0)
            return 0;
        return await _db.Users.CountAsync(u => ids.Contains(u.Id) && u.IsActive, ct);
    }

    /// <summary>Amigos em comum entre quem vê e o perfil: até 6 com foto/apelido e o total.</summary>
    public async Task<(List<FriendUserResponse> Items, int Total)> MutualFriendsAsync(
        Guid viewerId, Guid targetId, CancellationToken ct = default)
    {
        if (viewerId == targetId)
            return ([], 0);
        var mine = await FriendIdsAsync(viewerId, ct);
        if (mine.Count == 0)
            return ([], 0);
        var theirs = await FriendIdsAsync(targetId, ct);
        var common = mine.Intersect(theirs).ToList();
        if (common.Count == 0)
            return ([], 0);
        var users = await UsersAsync(common, activeOnly: true, ct);
        var items = users.Values
            .Select(u => Fill(new FriendUserResponse(), u))
            .OrderBy(u => u.DisplayName, StringComparer.Create(PtBr, ignoreCase: true))
            .ThenBy(u => u.Handle, StringComparer.Ordinal)
            .ToList();
        return (items.Take(MaxMutualFriends).ToList(), items.Count);
    }

    private async Task<HashSet<Guid>> FriendIdsAsync(Guid userId, CancellationToken ct)
    {
        var accepted = nameof(FriendshipStatus.Accepted);
        var ids = await _db.Friendships.AsNoTracking()
            .Where(f => f.Status == accepted && (f.UserLowId == userId || f.UserHighId == userId))
            .Select(f => f.UserLowId == userId ? f.UserHighId : f.UserLowId)
            .ToListAsync(ct);
        return [.. ids];
    }

    // ------------------------------------------------------------------ jogando agora

    /// <summary>
    /// Para cada amigo (id), o torneio em andamento em que ele está jogando (inscrição vinculada Registered/Active) e
    /// que <paramref name="viewerId"/> pode acompanhar. Amigo em torneio que quem vê não pode abrir não entra.
    /// </summary>
    private async Task<Dictionary<Guid, FriendPlayingNowResponse>> PlayingNowAsync(
        Guid viewerId, List<Guid> friendIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, FriendPlayingNowResponse>();
        var inProgress = nameof(TournamentStatus.InProgress);
        var rows = await _db.TournamentEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Person.UserId != null
                && friendIds.Contains(e.Person.UserId.Value)
                && PlayingEntryStatuses.Contains(e.Status)
                && e.Tournament.Status == inProgress
                && e.Tournament.DeletedAt == null
                && e.Tournament.HomeGame.DeletedAt == null
                && e.Tournament.HomeGame.IsActive)
            .Select(e => new
            {
                UserId = e.Person.UserId!.Value,
                e.TournamentId,
                TournamentName = e.Tournament.Name,
                e.Tournament.HomeGameId,
                HomeGameName = e.Tournament.HomeGame.Name,
                e.Tournament.HomeGame.IsPrimary,
                e.Tournament.HomeGame.OwnerId,
                OwnerName = e.Tournament.HomeGame.Owner.FullName,
            })
            .ToListAsync(ct);
        if (rows.Count == 0)
            return result;

        var tournamentIds = rows.Select(r => r.TournamentId).Distinct().ToList();
        // Quem vê está inscrito (vinculado) nesses torneios → área do jogador
        var viewerTournaments = (await _db.TournamentEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Person.UserId == viewerId && tournamentIds.Contains(e.TournamentId))
            .Select(e => e.TournamentId)
            .ToListAsync(ct)).ToHashSet();
        // Quem vê é dono ou membro ativo do clube → painel do torneio
        var homeGameIds = rows.Select(r => r.HomeGameId).Distinct().ToList();
        var viewerHomeGames = (await _db.HomeGames.AsNoTracking()
            .Where(h => homeGameIds.Contains(h.Id) && h.OwnerId == viewerId)
            .Select(h => h.Id)
            .Union(_db.HomeGameMembers.AsNoTracking()
                .Where(m => homeGameIds.Contains(m.HomeGameId) && m.UserId == viewerId && m.IsActive)
                .Select(m => m.HomeGameId))
            .ToListAsync(ct)).ToHashSet();

        foreach (var group in rows.GroupBy(r => r.UserId))
        {
            foreach (var r in group.OrderBy(r => r.TournamentName, StringComparer.Create(PtBr, ignoreCase: true)))
            {
                string? link = null;
                if (viewerTournaments.Contains(r.TournamentId))
                    link = TournamentNotifier.MyTournamentLink(r.TournamentId);
                else if (viewerHomeGames.Contains(r.HomeGameId))
                    link = $"/tournaments/{r.TournamentId}";
                if (link is null)
                    continue;

                var ownerPlan = await _plans.GetUserEffectivePlanAsync(r.OwnerId, ct);
                var label = HomeGamePrimary.DisplayNameFor(r.IsPrimary, ownerPlan, r.HomeGameName)
                    ?? (r.OwnerId == viewerId
                        ? MePlayerService.OwnLabel
                        : string.IsNullOrWhiteSpace(r.OwnerName) ? r.HomeGameName : $"Torneios de {r.OwnerName.Trim()}");
                result[group.Key] = new FriendPlayingNowResponse
                {
                    TournamentId = r.TournamentId,
                    TournamentName = r.TournamentName,
                    HomeGameLabel = label,
                    Link = link,
                };
                break;
            }
        }
        return result;
    }

    // ------------------------------------------------------------------ apoio

    private sealed record UserRow(Guid Id, string Handle, string? Nickname, string FullName, string? PhotoUrl, string? City, string? State);

    private async Task<Dictionary<Guid, UserRow>> UsersAsync(List<Guid> ids, bool activeOnly, CancellationToken ct)
    {
        var query = _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id));
        if (activeOnly)
            query = query.Where(u => u.IsActive && u.EmailConfirmedAt != null);
        return await query
            .Select(u => new UserRow(u.Id, u.Handle, u.Nickname, u.FullName, u.PhotoUrl, u.City, u.State))
            .ToDictionaryAsync(u => u.Id, ct);
    }

    private static T Fill<T>(T target, UserRow u) where T : FriendUserResponse
    {
        target.Id = u.Id;
        target.Handle = u.Handle;
        target.DisplayName = PlayerProfileService.DisplayNameOf(u.Nickname, u.FullName);
        target.PhotoUrl = u.PhotoUrl;
        target.City = u.City;
        target.State = u.State;
        return target;
    }

    private async Task<string> DisplayNameAsync(Guid userId, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.Nickname, x.FullName })
            .FirstAsync(ct);
        return PlayerProfileService.DisplayNameOf(u.Nickname, u.FullName);
    }

    private sealed record TargetRow(Guid Id, string FriendRequestsFrom);

    /// <summary>Conta ativa e confirmada pelo handle (normalizado); null quando não existe.</summary>
    private async Task<TargetRow?> FindActiveByHandleAsync(string handle, CancellationToken ct)
    {
        var key = NormalizeHandle(handle);
        if (key.Length == 0 || key.Length > HandleGenerator.MaxLength)
            return null;
        return await _db.Users.AsNoTracking()
            .Where(u => u.Handle == key && u.IsActive && u.EmailConfirmedAt != null)
            .Select(u => new TargetRow(u.Id, u.FriendRequestsFrom))
            .FirstOrDefaultAsync(ct);
    }

    private static string NormalizeHandle(string? handle) =>
        (handle ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();

    private Task<Friendship?> FindPairAsync(Guid a, Guid b, CancellationToken ct, bool track = false)
    {
        var (low, high) = Friendship.Order(a, b);
        var query = track ? _db.Friendships : _db.Friendships.AsNoTracking();
        return query.FirstOrDefaultAsync(f => f.UserLowId == low && f.UserHighId == high, ct);
    }
}
