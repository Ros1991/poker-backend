using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>Resultado do envio: a mensagem e para quem o controller difunde (<c>MessageReceived</c> nos dois grupos <c>user_{id}</c>).</summary>
public record SendMessageResult(MessageResponse Message, Guid SenderId, Guid RecipientId);

/// <summary>Resultado de "lida": até quando leu e a outra conta (que recebe <c>MessageRead</c>).</summary>
public record ReadConversationResult(DateTimeOffset ReadAt, Guid OtherUserId);

/// <summary>Resultado de apagar: a conversa, a mensagem e a outra conta (que recebe <c>MessageDeleted</c>).</summary>
public record DeleteMessageResult(Guid ConversationId, Guid MessageId, Guid OtherUserId);

/// <summary>
/// Mensagens 1 para 1 (SPEC-20261108-1638, plano §5, D8 e D11).
/// <para>
/// <b>Quem pode abrir conversa direta</b> (<see cref="OpenDirectAsync"/>): amigos (<see cref="IFriendshipLookup.AreFriendsAsync"/>);
/// com <c>MessagesFrom = FriendsAndOrganizers</c> de quem recebe, também o dono ou membro administrador de um home game em
/// que quem recebe tem jogador vinculado (<c>Person.UserId</c>). Bloqueio em qualquer sentido responde como se a conta não
/// existisse (null → 404) em tudo: abrir, listar, ler, enviar. Uma conversa direta por par; reabrir devolve a existente.
/// <b>A regra vale enquanto a conversa durar</b> (revisão da onda 2, M1): desfazer a amizade ou mudar <c>MessagesFrom</c>
/// encerra a conversa — <see cref="SendAsync"/>, <see cref="OpenDirectAsync"/> e <see cref="CanOpenDirectAsync"/>
/// reaplicam a regra (<see cref="ConversationClosedException"/>, 400 com <c>code: conversation_closed</c>); ler continua.
/// <b>Motivo genérico</b> (B3): quem não vê o perfil (<c>ProfileVisibility</c> Nobody, ou Friends sem amizade) recebe só
/// "não recebe mensagens de você", nunca a preferência de mensagens de quem recebe.
/// <b>Conversa de pedido de vaga</b> (<see cref="OpenForTournamentRequestAsync"/>): organizador e interessado, sem
/// amizade, enquanto o pedido existir (<see cref="ITournamentRequestLookup"/> — nula até a SPEC-20261108-1639); fechado
/// o pedido, enviar também é recusado.
/// </para>
/// <para>
/// <b>Limites</b> (contados no banco): 60 mensagens por hora por conta e 20 conversas novas por dia, respondidos com 429
/// (<see cref="TooManyMessagesException"/>, <see cref="TooManyConversationsException"/>). <b>Notificação</b>
/// <c>MessageReceived</c> só quando nenhuma conexão de quem recebe está com a conversa aberta (<see cref="IChatPresence"/>) e
/// no máximo uma não lida por conversa (a existente é atualizada e empurrada de novo). <b>Não lidas</b> = mensagens da outra
/// conta depois do <c>LastReadAt</c> de quem vê; o contador do envelope soma as CONVERSAS com não lida.
/// </para>
/// </summary>
public class ChatService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    public const int MaxMessagesPerHour = 60;
    public const int MaxNewConversationsPerDay = 20;
    public const int NotificationPreviewLength = 120;

    public const string ConversationNotFoundMessage = "Conversa não encontrada.";
    public const string MessageNotFoundMessage = "Mensagem não encontrada.";
    public const string RequestNotFoundMessage = "Pedido de vaga não encontrado.";
    public const string SelfMessage = "Você não pode mandar mensagem para si mesmo.";
    public const string OnlyFriendsMessage = "Só amigos podem trocar mensagens com este jogador. Peça a amizade primeiro.";
    public const string OnlyFriendsAndOrganizersMessage =
        "Só amigos e organizadores de clubes em que ele joga podem trocar mensagens com este jogador. Peça a amizade primeiro.";
    public const string EmptyBodyMessage = "Escreva a mensagem antes de enviar.";
    public const string BodyTooLongMessage = "A mensagem pode ter no máximo 2.000 caracteres.";
    public const string InactiveRecipientMessage = "Esta conta não está mais ativa.";
    public const string OpenNeedsTargetMessage = "Informe a conta (userId) ou o pedido de vaga (tournamentRequestId).";
    /// <summary>B3: para quem não vê o perfil de quem recebe, sem revelar a preferência de mensagens dele.</summary>
    public const string PrivateRecipientMessage = "Este jogador não recebe mensagens de você.";
    /// <summary>M1: a conversa direta existe, mas a regra deixou de valer (amizade desfeita ou MessagesFrom mudou).</summary>
    public const string ConversationClosedMessage =
        "Vocês não são mais amigos, ou este jogador mudou quem pode mandar mensagem a ele. Peça a amizade de novo para continuar a conversa.";
    /// <summary>M1: a conversa do pedido de vaga existe, mas o pedido não está mais em aberto.</summary>
    public const string RequestClosedMessage = "O pedido de vaga deste torneio não está mais em aberto, e esta conversa foi encerrada.";
    public const string RaceMessage = "A conversa acabou de ser criada. Recarregue a página.";
    public const string DeletedAccountName = "Conta excluída";

    private static readonly TimeSpan HourWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan DayWindow = TimeSpan.FromHours(24);

    private readonly IAppDbContext _db;
    private readonly IFriendshipLookup _friends;
    private readonly ITournamentRequestLookup _requests;
    private readonly IChatPresence _presence;
    private readonly NotificationService _notifications;
    private readonly INotificationPusher _pusher;

    public ChatService(
        IAppDbContext db,
        IFriendshipLookup friends,
        ITournamentRequestLookup requests,
        IChatPresence presence,
        NotificationService notifications,
        INotificationPusher pusher)
    {
        _db = db;
        _friends = friends;
        _requests = requests;
        _presence = presence;
        _notifications = notifications;
        _pusher = pusher;
    }

    /// <summary>Caminho da conversa no site (link da notificação).</summary>
    public static string ConversationLink(Guid conversationId) => $"/mensagens/{conversationId}";

    // ------------------------------------------------------------------ abrir

    /// <summary>
    /// <c>POST conversations {{ userId }}</c>. Null = conta inexistente ou bloqueio em qualquer sentido (404).
    /// <see cref="DomainException"/> (400) quando a regra de quem pode não deixa; 429 no limite diário.
    /// </summary>
    public async Task<OpenConversationResponse?> OpenDirectAsync(Guid userId, Guid otherUserId, CancellationToken ct = default)
    {
        if (otherUserId == userId)
            throw new DomainException(SelfMessage);
        var other = await _db.Users.AsNoTracking()
            .Where(u => u.Id == otherUserId)
            .Select(u => new { u.Id, u.IsActive, u.EmailConfirmedAt, u.MessagesFrom, u.ProfileVisibility })
            .FirstOrDefaultAsync(ct);
        if (other is null || await _friends.IsBlockedAsync(userId, otherUserId, ct))
            return null;

        var (low, high) = Conversation.Order(userId, otherUserId);
        var direct = nameof(ConversationType.Direct);
        var existing = await _db.Conversations.AsNoTracking()
            .Where(c => c.Type == direct && c.UserLowId == low && c.UserHighId == high)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (existing != Guid.Empty)
        {
            // M1: a conversa existe, mas a regra tem que valer ainda (conta inativa: devolve para ler; enviar recusa)
            if (other.IsActive && other.EmailConfirmedAt is not null
                && await CannotOpenReasonAsync(userId, otherUserId, other.MessagesFrom, other.ProfileVisibility, ct) is not null)
                throw new ConversationClosedException(ConversationClosedMessage);
            return new OpenConversationResponse { Conversation = (await GetAsync(userId, existing, ct))!, Created = false };
        }

        if (!other.IsActive || other.EmailConfirmedAt is null)
            throw new DomainException(InactiveRecipientMessage);
        var reason = await CannotOpenReasonAsync(userId, otherUserId, other.MessagesFrom, other.ProfileVisibility, ct);
        if (reason is not null)
            throw new DomainException(reason);

        var id = await CreateAsync(userId, otherUserId, direct, null, ct);
        return new OpenConversationResponse { Conversation = (await GetAsync(userId, id, ct))!, Created = true };
    }

    /// <summary>
    /// <c>GET me/conversations/with/{userId}</c>: o botão "Enviar mensagem" do perfil e a caixa de envio da conversa. Sem
    /// criar nada: diz se pode abrir (ou se a conversa já existe) e, se não pode, o motivo. Conta inexistente ou bloqueada
    /// → não pode, sem motivo. Conversa existente com a regra já inválida (M1) → <c>canOpen false</c> com o id dela e o
    /// motivo de conversa encerrada.
    /// </summary>
    public async Task<CanMessageResponse> CanOpenDirectAsync(Guid userId, Guid otherUserId, CancellationToken ct = default)
    {
        var response = new CanMessageResponse();
        if (otherUserId == userId)
            return response;
        var other = await _db.Users.AsNoTracking()
            .Where(u => u.Id == otherUserId && u.IsActive && u.EmailConfirmedAt != null)
            .Select(u => new { u.MessagesFrom, u.ProfileVisibility })
            .FirstOrDefaultAsync(ct);
        if (other is null || await _friends.IsBlockedAsync(userId, otherUserId, ct))
            return response;

        var (low, high) = Conversation.Order(userId, otherUserId);
        var direct = nameof(ConversationType.Direct);
        var existing = await _db.Conversations.AsNoTracking()
            .Where(c => c.Type == direct && c.UserLowId == low && c.UserHighId == high)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        var reason = await CannotOpenReasonAsync(userId, otherUserId, other.MessagesFrom, other.ProfileVisibility, ct);
        if (existing is not null)
            return new CanMessageResponse { CanOpen = reason is null, ConversationId = existing, Reason = reason is null ? null : ConversationClosedMessage };
        return new CanMessageResponse { CanOpen = reason is null, Reason = reason };
    }

    /// <summary>
    /// Null quando pode abrir conversa direta; senão a mensagem do motivo. Amigos sempre podem; com
    /// <c>FriendsAndOrganizers</c>, o organizador do clube também. Quem não vê o perfil de quem recebe
    /// (<paramref name="recipientVisibility"/> diferente de Everyone — aqui já se sabe que não são amigos) recebe o motivo
    /// genérico (B3), sem a preferência de mensagens.
    /// </summary>
    private async Task<string?> CannotOpenReasonAsync(
        Guid senderId, Guid recipientId, string recipientPreference, string recipientVisibility, CancellationToken ct)
    {
        if (await _friends.AreFriendsAsync(senderId, recipientId, ct))
            return null;
        var organizersToo = recipientPreference == nameof(MessagesFrom.FriendsAndOrganizers);
        if (organizersToo && await IsOrganizerOfAsync(senderId, recipientId, ct))
            return null;
        if (recipientVisibility != nameof(ProfileVisibility.Everyone))
            return PrivateRecipientMessage;
        return organizersToo ? OnlyFriendsAndOrganizersMessage : OnlyFriendsMessage;
    }

    /// <summary>
    /// M1: a conversa só continua enquanto a regra de abrir valer. Direta: a mesma regra de <see cref="CannotOpenReasonAsync"/>
    /// (amizade, ou organizador liberado). Pedido de vaga: o pedido ainda em aberto (qualquer um dos dois pode ser o
    /// organizador). Recusa com <see cref="ConversationClosedException"/> (400, <c>conversation_closed</c>).
    /// </summary>
    private async Task EnsureStillOpenAsync(
        Conversation conversation, Guid senderId, Guid otherId, string otherPreference, string otherVisibility, CancellationToken ct)
    {
        if (conversation.Type == nameof(ConversationType.TournamentRequest))
        {
            var tournamentId = conversation.TournamentId ?? Guid.Empty;
            var open = await _requests.HasOpenRequestAsync(tournamentId, otherId, senderId, ct)
                || await _requests.HasOpenRequestAsync(tournamentId, senderId, otherId, ct);
            if (!open)
                throw new ConversationClosedException(RequestClosedMessage);
            return;
        }
        if (await CannotOpenReasonAsync(senderId, otherId, otherPreference, otherVisibility, ct) is not null)
            throw new ConversationClosedException(ConversationClosedMessage);
    }

    /// <summary>
    /// <paramref name="organizerId"/> é dono ou membro administrador (ativo) de um home game ativo em que
    /// <paramref name="playerUserId"/> tem jogador vinculado (<c>Person.UserId</c>).
    /// </summary>
    public async Task<bool> IsOrganizerOfAsync(Guid organizerId, Guid playerUserId, CancellationToken ct = default)
    {
        var homeGames = _db.Persons
            .Where(p => p.UserId == playerUserId && p.HomeGame.IsActive)
            .Select(p => p.HomeGameId);
        if (await _db.HomeGames.AnyAsync(h => homeGames.Contains(h.Id) && h.OwnerId == organizerId, ct))
            return true;
        return await _db.HomeGameMembers.AnyAsync(
            m => homeGames.Contains(m.HomeGameId) && m.UserId == organizerId && m.IsAdmin && m.IsActive, ct);
    }

    /// <summary>
    /// Conversa organizador e interessado de um pedido de vaga (SPEC-20261108-1639), aberta por qualquer um dos dois. Null
    /// = não há pedido em aberto, quem chama não é um dos dois, ou bloqueio (404). Uma por par e torneio.
    /// </summary>
    public async Task<OpenConversationResponse?> OpenForTournamentRequestAsync(
        Guid tournamentId, Guid organizerUserId, Guid requesterUserId, Guid callerUserId, CancellationToken ct = default)
    {
        if (callerUserId != organizerUserId && callerUserId != requesterUserId)
            return null;
        if (organizerUserId == requesterUserId)
            throw new DomainException(SelfMessage);
        if (!await _requests.HasOpenRequestAsync(tournamentId, organizerUserId, requesterUserId, ct))
            return null;
        if (await _friends.IsBlockedAsync(organizerUserId, requesterUserId, ct))
            return null;

        var otherId = callerUserId == organizerUserId ? requesterUserId : organizerUserId;
        var (low, high) = Conversation.Order(organizerUserId, requesterUserId);
        var type = nameof(ConversationType.TournamentRequest);
        var existing = await _db.Conversations.AsNoTracking()
            .Where(c => c.Type == type && c.UserLowId == low && c.UserHighId == high && c.TournamentId == tournamentId)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (existing != Guid.Empty)
            return new OpenConversationResponse { Conversation = (await GetAsync(callerUserId, existing, ct))!, Created = false };

        var id = await CreateAsync(callerUserId, otherId, type, tournamentId, ct);
        return new OpenConversationResponse { Conversation = (await GetAsync(callerUserId, id, ct))!, Created = true };
    }

    /// <summary><c>POST conversations {{ tournamentRequestId }}</c>: resolve o pedido pela costura e abre a conversa dele.</summary>
    public async Task<OpenConversationResponse?> OpenByRequestIdAsync(Guid requestId, Guid callerUserId, CancellationToken ct = default)
    {
        var request = await _requests.FindOpenRequestAsync(requestId, callerUserId, ct);
        if (request is null)
            return null;
        return await OpenForTournamentRequestAsync(request.TournamentId, request.OrganizerUserId, request.RequesterUserId, callerUserId, ct);
    }

    /// <summary>Cria a conversa e os dois membros (limite diário conferido antes). Corrida no índice único → recarrega a existente.</summary>
    private async Task<Guid> CreateAsync(Guid creatorId, Guid otherId, string type, Guid? tournamentId, CancellationToken ct)
    {
        await EnsureDailyLimitAsync(creatorId, ct);
        var (low, high) = Conversation.Order(creatorId, otherId);
        var conversation = new Conversation
        {
            Type = type,
            TournamentId = tournamentId,
            UserLowId = low,
            UserHighId = high,
            CreatedById = creatorId,
        };
        _db.Conversations.Add(conversation);
        _db.ConversationMembers.Add(new ConversationMember { Conversation = conversation, UserId = creatorId, LastReadAt = DateTimeOffset.UtcNow });
        _db.ConversationMembers.Add(new ConversationMember { Conversation = conversation, UserId = otherId });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            // Os dois abriram ao mesmo tempo e o outro gravou primeiro
            var existing = await _db.Conversations.AsNoTracking()
                .Where(c => c.Type == type && c.UserLowId == low && c.UserHighId == high && c.TournamentId == tournamentId)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            return existing ?? throw new DomainException(RaceMessage);
        }
        return conversation.Id;
    }

    private async Task EnsureDailyLimitAsync(Guid userId, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow - DayWindow;
        var recent = await _db.Conversations.AsNoTracking()
            .Where(c => c.CreatedById == userId && c.CreatedAt > since)
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);
        if (recent.Count < MaxNewConversationsPerDay)
            return;
        var retryAfter = (int)Math.Ceiling((recent[0] + DayWindow - DateTimeOffset.UtcNow).TotalSeconds);
        throw new TooManyConversationsException(retryAfter);
    }

    // ------------------------------------------------------------------ listar e ler

    /// <summary><c>GET me/conversations</c>: as conversas da conta, da mais recente, sem as de contas bloqueadas.</summary>
    public async Task<List<ConversationResponse>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var blocked = (await _friends.BlockedUserIdsAsync(userId, ct)).ToList();
        var rows = await Project(_db.Conversations.AsNoTracking()
                .Where(c => c.UserLowId == userId || c.UserHighId == userId), userId)
            .ToListAsync(ct);
        rows = rows.Where(r => !blocked.Contains(r.OtherId)).ToList();
        if (rows.Count == 0)
            return [];
        var users = await UsersAsync(rows.Select(r => r.OtherId).Distinct().ToList(), ct);
        return rows
            .OrderByDescending(r => r.LastMessageAt ?? r.CreatedAt)
            .Select(r => ToResponse(r, users))
            .ToList();
    }

    /// <summary><c>GET conversations/{id}</c>: uma conversa da conta. Null = não existe, não é dela ou bloqueio (404).</summary>
    public async Task<ConversationResponse?> GetAsync(Guid userId, Guid conversationId, CancellationToken ct = default)
    {
        var row = await Project(_db.Conversations.AsNoTracking()
                .Where(c => c.Id == conversationId && (c.UserLowId == userId || c.UserHighId == userId)), userId)
            .FirstOrDefaultAsync(ct);
        if (row is null || await _friends.IsBlockedAsync(userId, row.OtherId, ct))
            return null;
        var users = await UsersAsync([row.OtherId], ct);
        return ToResponse(row, users);
    }

    /// <summary>
    /// <c>GET conversations/{id}/messages?before=&amp;limit=</c>: página do histórico, das mais recentes para trás
    /// (<paramref name="before"/> = <c>sentAt</c> da mais antiga já carregada), devolvida em ordem cronológica.
    /// </summary>
    public async Task<MessagePageResponse?> ListMessagesAsync(
        Guid userId, Guid conversationId, DateTimeOffset? before, int limit, CancellationToken ct = default)
    {
        var size = limit <= 0 ? DefaultPageSize : Math.Min(limit, MaxPageSize);
        if (await FindOtherAsync(userId, conversationId, ct) is null)
            return null;

        var query = _db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        if (before is { } cursor)
            query = query.Where(m => m.SentAt < cursor);
        var items = await query
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.CreatedAt)
            .Take(size + 1)
            .Select(m => new { m.Id, m.ConversationId, m.SenderId, m.Body, m.SentAt, m.DeletedAt })
            .ToListAsync(ct);
        var hasMore = items.Count > size;
        if (hasMore)
            items.RemoveAt(items.Count - 1);
        items.Reverse();
        return new MessagePageResponse
        {
            Items = items.Select(m => ToMessage(m.Id, m.ConversationId, m.SenderId, m.Body, m.SentAt, m.DeletedAt)).ToList(),
            HasMore = hasMore,
        };
    }

    /// <summary>
    /// <c>GET me/conversations/unread-count</c>: quantas conversas têm mensagem da outra conta depois de "lida até".
    /// Mensagem apagada (pelo autor, ou pela exclusão da conta dele — A2) não conta como não lida.
    /// </summary>
    public async Task<int> CountUnreadConversationsAsync(Guid userId, CancellationToken ct = default)
    {
        var blocked = (await _friends.BlockedUserIdsAsync(userId, ct)).ToList();
        return await _db.Conversations.AsNoTracking()
            .Where(c => (c.UserLowId == userId || c.UserHighId == userId)
                && !blocked.Contains(c.UserLowId == userId ? c.UserHighId : c.UserLowId)
                && c.Messages.Any(m => m.SenderId != userId && m.DeletedAt == null
                    && !c.Members.Any(mm => mm.UserId == userId && mm.LastReadAt != null && mm.LastReadAt >= m.SentAt)))
            .CountAsync(ct);
    }

    /// <summary>A outra conta da conversa, se <paramref name="userId"/> é membro e não há bloqueio; senão null.</summary>
    public async Task<Guid?> FindOtherAsync(Guid userId, Guid conversationId, CancellationToken ct = default)
    {
        var pair = await _db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId && (c.UserLowId == userId || c.UserHighId == userId))
            .Select(c => new { c.UserLowId, c.UserHighId })
            .FirstOrDefaultAsync(ct);
        if (pair is null)
            return null;
        var other = pair.UserLowId == userId ? pair.UserHighId : pair.UserLowId;
        return await _friends.IsBlockedAsync(userId, other, ct) ? null : other;
    }

    // ------------------------------------------------------------------ enviar, ler, apagar

    /// <summary>
    /// <c>POST conversations/{id}/messages</c>. Null = conversa inexistente, de outra conta ou bloqueio (404). 400 com texto
    /// vazio/longo demais, conta da outra pessoa inativa ou conversa encerrada (M1, <see cref="ConversationClosedException"/>);
    /// 429 acima de 60 por hora. Avança o "lida até" de quem envia, atualiza <c>LastMessageAt</c> e cuida da notificação
    /// (só com a conversa fechada do outro lado; uma não lida por conversa).
    /// </summary>
    public async Task<SendMessageResult?> SendAsync(Guid userId, Guid conversationId, string? body, CancellationToken ct = default)
    {
        var text = (body ?? string.Empty).Trim();
        if (text.Length == 0)
            throw new DomainException(EmptyBodyMessage);
        if (text.Length > ChatMessage.BodyMaxLength)
            throw new DomainException(BodyTooLongMessage);

        var conversation = await _db.Conversations
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == conversationId && (c.UserLowId == userId || c.UserHighId == userId), ct);
        if (conversation is null)
            return null;
        var otherId = conversation.OtherOf(userId);
        if (await _friends.IsBlockedAsync(userId, otherId, ct))
            return null;
        var other = await _db.Users.AsNoTracking()
            .Where(u => u.Id == otherId)
            .Select(u => new { u.IsActive, u.EmailConfirmedAt, u.MessagesFrom, u.ProfileVisibility })
            .FirstOrDefaultAsync(ct);
        if (other is null || !other.IsActive || other.EmailConfirmedAt is null)
            throw new DomainException(InactiveRecipientMessage);
        await EnsureStillOpenAsync(conversation, userId, otherId, other.MessagesFrom, other.ProfileVisibility, ct);

        await EnsureHourlyLimitAsync(userId, ct);

        var now = DateTimeOffset.UtcNow;
        var message = new ChatMessage
        {
            ConversationId = conversationId,
            SenderId = userId,
            Body = text,
            SentAt = now,
        };
        _db.Messages.Add(message);
        conversation.LastMessageAt = now;
        var mine = conversation.Members.FirstOrDefault(m => m.UserId == userId);
        if (mine is not null)
            mine.LastReadAt = now;

        // Notificação: só com a conversa fechada em todas as conexões de quem recebe; no máximo uma não lida por conversa
        Notification? updated = null;
        if (!_presence.IsConversationOpen(otherId, conversationId))
        {
            var link = ConversationLink(conversationId);
            var kind = nameof(NotificationKind.MessageReceived);
            var senderName = await DisplayNameAsync(userId, ct);
            var existing = await _db.Notifications
                .FirstOrDefaultAsync(n => n.UserId == otherId && n.Kind == kind && n.Link == link && n.ReadAt == null, ct);
            if (existing is not null)
            {
                var theirLastRead = conversation.Members.FirstOrDefault(m => m.UserId == otherId)?.LastReadAt;
                var unread = 1 + await _db.Messages.CountAsync(
                    m => m.ConversationId == conversationId && m.SenderId == userId
                        && (theirLastRead == null || m.SentAt > theirLastRead), ct);
                existing.Title = $"Mensagens de {senderName}";
                existing.Body = $"{unread} mensagens novas. Última: {Preview(text)}";
                existing.CreatedAt = now; // volta ao topo do sino (o contexto só preenche CreatedAt ao inserir)
                updated = existing;
            }
            else
            {
                await _notifications.StageAsync([otherId], NotificationKind.MessageReceived,
                    $"Mensagem de {senderName}", Preview(text), link, ct);
            }
        }

        await _db.SaveChangesAsync(ct);
        await _notifications.PushPendingAsync(ct);
        if (updated is not null)
        {
            try
            {
                var unreadNotifications = await _notifications.CountUnreadAsync(otherId, ct);
                await _pusher.PushAsync(otherId, new NotificationPush(
                    updated.Id, updated.Kind, updated.Title, updated.Body, updated.Link, updated.CreatedAt, unreadNotifications), ct);
            }
            catch (Exception)
            {
                // a gravação já valeu; o sino recarrega ao reconectar
            }
        }

        return new SendMessageResult(
            ToMessage(message.Id, conversationId, userId, message.Body, message.SentAt, null), userId, otherId);
    }

    private async Task EnsureHourlyLimitAsync(Guid userId, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow - HourWindow;
        var recent = await _db.Messages.AsNoTracking()
            .Where(m => m.SenderId == userId && m.SentAt > since)
            .OrderBy(m => m.SentAt)
            .Select(m => m.SentAt)
            .ToListAsync(ct);
        if (recent.Count < MaxMessagesPerHour)
            return;
        var retryAfter = (int)Math.Ceiling((recent[0] + HourWindow - DateTimeOffset.UtcNow).TotalSeconds);
        throw new TooManyMessagesException(retryAfter);
    }

    /// <summary><c>POST conversations/{id}/read</c>: "lida até agora". Null = não é desta conta ou bloqueio (404).</summary>
    public async Task<ReadConversationResult?> MarkReadAsync(Guid userId, Guid conversationId, CancellationToken ct = default)
    {
        var otherId = await FindOtherAsync(userId, conversationId, ct);
        if (otherId is null)
            return null;
        var member = await _db.ConversationMembers
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == userId, ct);
        if (member is null)
            return null;
        var now = DateTimeOffset.UtcNow;
        member.LastReadAt = now;
        // A notificação da conversa (se houver) também fica lida
        var link = ConversationLink(conversationId);
        var kind = nameof(NotificationKind.MessageReceived);
        await _db.Notifications
            .Where(n => n.UserId == userId && n.Kind == kind && n.Link == link && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now).SetProperty(n => n.UpdatedAt, now), ct);
        await _db.SaveChangesAsync(ct);
        return new ReadConversationResult(now, otherId.Value);
    }

    /// <summary>
    /// <c>DELETE messages/{id}</c>: só quem enviou apaga (vira "mensagem apagada" para os dois). Idempotente. Null = não
    /// existe ou não é desta conta (404).
    /// </summary>
    public async Task<DeleteMessageResult?> DeleteMessageAsync(Guid userId, Guid messageId, CancellationToken ct = default)
    {
        var message = await _db.Messages
            .Include(m => m.Conversation)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.SenderId == userId, ct);
        if (message is null)
            return null;
        if (message.DeletedAt is null)
        {
            message.DeletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return new DeleteMessageResult(message.ConversationId, message.Id, message.Conversation.OtherOf(userId));
    }

    // ------------------------------------------------------------------ apoio

    private sealed class LastMessageRow
    {
        public Guid Id { get; init; }
        public Guid SenderId { get; init; }
        public string Body { get; init; } = string.Empty;
        public DateTimeOffset SentAt { get; init; }
        public DateTimeOffset? DeletedAt { get; init; }
    }

    private sealed class ConversationRow
    {
        public Guid Id { get; init; }
        public string Type { get; init; } = string.Empty;
        public Guid? TournamentId { get; init; }
        public string? TournamentName { get; init; }
        public Guid OtherId { get; init; }
        public DateTimeOffset? LastMessageAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? MyLastReadAt { get; init; }
        public DateTimeOffset? OtherLastReadAt { get; init; }
        public LastMessageRow? Last { get; init; }
        public int Unread { get; init; }
    }

    private static IQueryable<ConversationRow> Project(IQueryable<Conversation> query, Guid userId) =>
        query.Select(c => new ConversationRow
        {
            Id = c.Id,
            Type = c.Type,
            TournamentId = c.TournamentId,
            TournamentName = c.Tournament != null ? c.Tournament.Name : null,
            OtherId = c.UserLowId == userId ? c.UserHighId : c.UserLowId,
            LastMessageAt = c.LastMessageAt,
            CreatedAt = c.CreatedAt,
            MyLastReadAt = c.Members.Where(m => m.UserId == userId).Select(m => m.LastReadAt).FirstOrDefault(),
            OtherLastReadAt = c.Members.Where(m => m.UserId != userId).Select(m => m.LastReadAt).FirstOrDefault(),
            Last = c.Messages
                .OrderByDescending(m => m.SentAt)
                .Select(m => new LastMessageRow { Id = m.Id, SenderId = m.SenderId, Body = m.Body, SentAt = m.SentAt, DeletedAt = m.DeletedAt })
                .FirstOrDefault(),
            Unread = c.Messages.Count(m => m.SenderId != userId && m.DeletedAt == null
                && !c.Members.Any(mm => mm.UserId == userId && mm.LastReadAt != null && mm.LastReadAt >= m.SentAt)),
        });

    private sealed record UserRow(Guid Id, string Handle, string? Nickname, string FullName, string? PhotoUrl, bool IsActive, bool Deleted);

    private async Task<Dictionary<Guid, UserRow>> UsersAsync(List<Guid> ids, CancellationToken ct) =>
        await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new UserRow(u.Id, u.Handle, u.Nickname, u.FullName, u.PhotoUrl, u.IsActive && u.EmailConfirmedAt != null,
                u.Email.EndsWith("@excluida.invalid")))
            .ToDictionaryAsync(u => u.Id, ct);

    private static ConversationResponse ToResponse(ConversationRow r, Dictionary<Guid, UserRow> users)
    {
        users.TryGetValue(r.OtherId, out var u);
        return new ConversationResponse
        {
            Id = r.Id,
            Type = r.Type,
            TournamentId = r.TournamentId,
            TournamentName = r.TournamentName,
            Other = new ChatUserResponse
            {
                Id = r.OtherId,
                Handle = u?.Handle ?? string.Empty,
                DisplayName = u is null || u.Deleted ? DeletedAccountName : PlayerProfileService.DisplayNameOf(u.Nickname, u.FullName),
                PhotoUrl = u is null || u.Deleted ? null : u.PhotoUrl,
                IsActive = u is not null && u.IsActive && !u.Deleted,
            },
            LastMessage = r.Last is null ? null : ToMessage(r.Last.Id, r.Id, r.Last.SenderId, r.Last.Body, r.Last.SentAt, r.Last.DeletedAt),
            LastMessageAt = r.LastMessageAt,
            UnreadCount = r.Unread,
            MyLastReadAt = r.MyLastReadAt,
            OtherLastReadAt = r.OtherLastReadAt,
            CreatedAt = r.CreatedAt,
        };
    }

    private static MessageResponse ToMessage(Guid id, Guid conversationId, Guid senderId, string body, DateTimeOffset sentAt, DateTimeOffset? deletedAt) =>
        new()
        {
            Id = id,
            ConversationId = conversationId,
            SenderId = senderId,
            Body = deletedAt is null ? body : null,
            SentAt = sentAt,
            IsDeleted = deletedAt is not null,
            DeletedAt = deletedAt,
        };

    private async Task<string> DisplayNameAsync(Guid userId, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.Nickname, x.FullName })
            .FirstAsync(ct);
        return PlayerProfileService.DisplayNameOf(u.Nickname, u.FullName);
    }

    /// <summary>Começo do texto para a notificação (uma linha, até 120 caracteres).</summary>
    private static string Preview(string text)
    {
        var oneLine = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return oneLine.Length <= NotificationPreviewLength ? oneLine : oneLine[..(NotificationPreviewLength - 1)] + "…";
    }
}
