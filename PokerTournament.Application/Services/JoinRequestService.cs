using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Publicity;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Pedido de vaga em torneio público (SPEC-20261108-1639, D7). O interessado (conta logada que NÃO gerencia o clube)
/// pede uma vez por torneio com um recado; quem gerencia o home game aprova — vira RESERVA: o jogador é criado no clube
/// com nome, apelido e e-mail da conta, já vinculado (<c>Person.UserId</c>, regra da SPEC-20261029-1725), ou
/// reaproveitado se a conta já tem jogador lá — sem inscrição nem transação; a reserva aparece na fila "Reservas" do
/// painel e do terminal até o organizador inscrever (o <c>EntryService</c> grava <c>EntryId</c> ao inscrever o jogador
/// reservado). Com as vagas (<c>MaxPlayers</c>) esgotadas, aprovar põe em espera e a primeira da fila sobe sozinha
/// quando uma reserva sai (cancelada pelo interessado ou removida pelo organizador). Recusado não pede de novo; cancelado
/// pode. Tudo notifica (SPEC-20261108-1635): <c>JoinRequestReceived</c> para quem gerencia; Approved/Declined/Waitlisted/
/// Promoted para o interessado.
/// <para>
/// É também a implementação real de <see cref="ITournamentRequestLookup"/> (a costura das mensagens, SPEC-20261108-1638):
/// pedido aberto entre o interessado e quem gerencia o clube → a conversa <c>TournamentRequest</c> pode existir.
/// </para>
/// Quem chama confere o acesso: pedir/cancelar/"meu pedido" por qualquer conta logada (token de dealer não chega);
/// listar/aprovar/recusar/remover por quem GERENCIA (HomeGameAccess.CanManageAsync); a lista de reservas também pelo dealer por QR.
/// </summary>
public class JoinRequestService : ITournamentRequestLookup
{
    public const string NotPublicMessage = "Este torneio não está disponível para pedidos de vaga.";
    public const string OverMessage = "Este torneio já aconteceu.";
    public const string ManagesMessage = "Você organiza este torneio: inscreva os jogadores pelo painel.";
    public const string AlreadyRegisteredMessage = "Você já está inscrito neste torneio.";
    public const string AlreadyOpenMessage = "Você já pediu vaga neste torneio.";
    public const string DeclinedBeforeMessage = "O organizador recusou o seu pedido neste torneio; não dá para pedir de novo.";
    public const string MessageTooLongMessage = "O recado pode ter no máximo 500 caracteres.";
    public const string RequestNotFoundMessage = "Pedido de vaga não encontrado.";
    public const string AlreadyEnteredMessage = "Você já foi inscrito neste torneio: fale com o organizador.";
    public const string NotPendingMessage = "Este pedido já foi decidido.";
    public const string NotReservationMessage = "Esta reserva não está mais na fila.";
    public const string InactiveAccountMessage = "A conta que pediu a vaga foi desativada ou excluída.";

    /// <summary>Torneio de hoje (ou das últimas horas) ainda conta como próximo.</summary>
    public const int UpcomingGraceHours = 12;

    public static readonly string Pending = nameof(JoinRequestStatus.Pending);
    public static readonly string Approved = nameof(JoinRequestStatus.Approved);
    public static readonly string Waitlisted = nameof(JoinRequestStatus.Waitlisted);
    public static readonly string Declined = nameof(JoinRequestStatus.Declined);
    public static readonly string Cancelled = nameof(JoinRequestStatus.Cancelled);
    public static readonly string[] OpenStatuses = [Pending, Approved, Waitlisted];

    private static readonly string Finished = nameof(TournamentStatus.Finished);
    private static readonly string CancelledTournament = nameof(TournamentStatus.Cancelled);

    private readonly IAppDbContext _db;
    private readonly PlanService _plans;
    private readonly NotificationService _notifications;

    public JoinRequestService(IAppDbContext db, PlanService plans, NotificationService notifications)
    {
        _db = db;
        _plans = plans;
        _notifications = notifications;
    }

    /// <summary>Link de "Meus torneios › Próximos" nas notificações do interessado.</summary>
    public const string UpcomingLink = "/meus-torneios#proximos";

    // ------------------------------------------------------------------ interessado

    /// <summary>Estado do botão "Quero participar" da página pública para a conta logada.</summary>
    public async Task<MyJoinStateResponse> GetMyStateAsync(Guid userId, Guid tournamentId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.AsNoTracking()
            .Where(t => t.Id == tournamentId)
            .Select(t => new { t.Id, t.HomeGameId, t.IsPublic, t.Status })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        var request = await Requests()
            .Where(r => r.TournamentId == tournamentId && r.UserId == userId)
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct);
        var registered = await _db.TournamentEntries.AnyAsync(e => e.TournamentId == tournamentId && e.Person.UserId == userId, ct);
        var manages = await HomeGameAccess.CanManageAsync(_db, tournament.HomeGameId, userId, false, ct);
        var over = tournament.Status == Finished || tournament.Status == CancelledTournament;
        var declined = await _db.TournamentJoinRequests.AnyAsync(r => r.TournamentId == tournamentId && r.UserId == userId && r.Status == Declined, ct);
        return new MyJoinStateResponse
        {
            TournamentId = tournamentId,
            Request = request is null ? null : Map(request),
            Registered = registered,
            Manages = manages,
            CanRequest = tournament.IsPublic && !over && !registered && !manages && !declined
                && (request is null || !OpenStatuses.Contains(request.Status)),
        };
    }

    /// <summary>
    /// Pedir vaga: torneio público e não encerrado; a conta não gerencia o clube, não está inscrita, não tem pedido aberto
    /// nem recusa anterior. Notifica quem gerencia (<c>JoinRequestReceived</c>, categoria Pedidos).
    /// </summary>
    public async Task<JoinRequestResponse> RequestAsync(Guid userId, CreateJoinRequestRequest input, CancellationToken ct = default)
    {
        var message = input.Message?.Trim();
        if (string.IsNullOrEmpty(message))
            message = null;
        else if (message.Length > TournamentJoinRequest.MessageMaxLength)
            throw new DomainException(MessageTooLongMessage);

        var tournament = await _db.Tournaments.AsNoTracking()
            .Where(t => t.Id == input.TournamentId && t.HomeGame.DeletedAt == null && t.HomeGame.IsActive)
            .Select(t => new { t.Id, t.Name, t.HomeGameId, t.IsPublic, t.Status })
            .FirstOrDefaultAsync(ct);
        if (tournament is null || !tournament.IsPublic || tournament.Status == CancelledTournament)
            throw new KeyNotFoundException(NotPublicMessage);
        if (tournament.Status == Finished)
            throw new DomainException(OverMessage);
        if (await HomeGameAccess.CanManageAsync(_db, tournament.HomeGameId, userId, false, ct))
            throw new DomainException(ManagesMessage);
        if (await _db.TournamentEntries.AnyAsync(e => e.TournamentId == tournament.Id && e.Person.UserId == userId, ct))
            throw new DomainException(AlreadyRegisteredMessage);

        var previous = await _db.TournamentJoinRequests.AsNoTracking()
            .Where(r => r.TournamentId == tournament.Id && r.UserId == userId)
            .Select(r => r.Status)
            .ToListAsync(ct);
        if (previous.Any(s => OpenStatuses.Contains(s)))
            throw new DomainException(AlreadyOpenMessage);
        if (previous.Contains(Declined))
            throw new DomainException(DeclinedBeforeMessage);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new DomainException(InactiveAccountMessage);

        var request = new TournamentJoinRequest
        {
            TournamentId = tournament.Id,
            UserId = userId,
            Message = message,
            Status = Pending,
            RequestedAt = DateTimeOffset.UtcNow,
        };
        _db.TournamentJoinRequests.Add(request);

        var who = DisplayName(user.FullName, user.Nickname);
        var body = message is null ? $"{who} quer jogar." : $"{who} quer jogar: “{Truncate(message, 160)}”";
        await _notifications.StageAsync(
            await ManagerIdsAsync(tournament.HomeGameId, ct),
            NotificationKind.JoinRequestReceived,
            $"Pedido de vaga: {tournament.Name}",
            body,
            PlayerLinkRequestService.DashboardLink(tournament.Id),
            ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Dois toques ao mesmo tempo: o outro pedido já entrou
            throw new DomainException(AlreadyOpenMessage);
        }
        await _notifications.PushPendingAsync(ct);

        request.User = user;
        return Map(request);
    }

    /// <summary>
    /// O interessado cancela o próprio pedido (aguardando, reservado ou na espera). Reserva já inscrita não cancela por
    /// aqui (fala com o organizador). Reserva cancelada libera a vaga: a primeira da espera sobe.
    /// </summary>
    public async Task<JoinRequestResponse> CancelAsync(Guid userId, Guid requestId, CancellationToken ct = default)
    {
        var request = await Requests(track: true).FirstOrDefaultAsync(r => r.Id == requestId && r.UserId == userId, ct)
            ?? throw new KeyNotFoundException(RequestNotFoundMessage);
        if (request.Status == Cancelled)
            return Map(request);
        if (!OpenStatuses.Contains(request.Status))
            throw new DomainException(NotPendingMessage);
        if (request.Status == Approved && request.EntryId is not null)
            throw new DomainException(AlreadyEnteredMessage);

        var wasApproved = request.Status == Approved;
        var wasWaitlisted = request.Status == Waitlisted;
        request.Status = Cancelled;
        request.WaitlistPosition = null;
        request.DecidedAt = DateTimeOffset.UtcNow;
        request.DecidedBy = null;

        if (wasWaitlisted)
            await RenumberWaitlistAsync(request.TournamentId, ct);
        if (wasApproved)
            await PromoteWaitlistAsync(request.TournamentId, ct);

        await _db.SaveChangesAsync(ct);
        await _notifications.PushPendingAsync(ct);
        return Map(request);
    }

    /// <summary>
    /// "Meus torneios › Próximos": torneios futuros com pedido aberto da conta ou inscrição vinculada ainda não começada,
    /// por data. Endereço completo e "Como chegar" só com reserva aprovada ou inscrição (D6).
    /// </summary>
    public async Task<List<MyUpcomingTournamentResponse>> UpcomingAsync(Guid userId, CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-UpcomingGraceHours);
        var draft = nameof(TournamentStatus.Draft);
        var open = nameof(TournamentStatus.OpenForRegistration);

        var requests = await _db.TournamentJoinRequests.AsNoTracking()
            .Where(r => r.UserId == userId && OpenStatuses.Contains(r.Status)
                && r.Tournament.Status != Finished && r.Tournament.Status != CancelledTournament
                && r.Tournament.Date >= since && r.Tournament.HomeGame.DeletedAt == null)
            .Select(r => new { r.Id, r.Status, r.WaitlistPosition, r.EntryId, r.TournamentId })
            .ToListAsync(ct);
        var entryTournamentIds = await _db.TournamentEntries.AsNoTracking()
            .Where(e => e.Person.UserId == userId
                && (e.Tournament.Status == draft || e.Tournament.Status == open)
                && e.Tournament.Date >= since && e.Tournament.HomeGame.DeletedAt == null)
            .Select(e => e.TournamentId)
            .Distinct()
            .ToListAsync(ct);

        var ids = requests.Select(r => r.TournamentId).Union(entryTournamentIds).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var tournaments = await _db.Tournaments.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new
            {
                t.Id, t.Name, t.Date, t.Status, t.HomeGameId, t.BuyInAmount, t.PublicSlug, t.IsPublic,
                HomeGameName = t.HomeGame.Name,
                HomeGameIsPrimary = t.HomeGame.IsPrimary,
                OwnerId = t.HomeGame.OwnerId,
                OwnerName = t.HomeGame.Owner.FullName,
                t.HomeGame.Street, t.HomeGame.Number, t.HomeGame.Complement, t.HomeGame.District, t.HomeGame.City, t.HomeGame.State,
                t.HomeGame.PostalCode, t.HomeGame.Latitude, t.HomeGame.Longitude, t.HomeGame.Location,
            })
            .ToListAsync(ct);

        var ownerPlans = new Dictionary<Guid, string>();
        foreach (var ownerId in tournaments.Select(t => t.OwnerId).Distinct())
            ownerPlans[ownerId] = await _plans.GetUserEffectivePlanAsync(ownerId, ct);
        var registeredIds = entryTournamentIds.ToHashSet();
        var byTournament = requests.ToDictionary(r => r.TournamentId);

        var list = new List<MyUpcomingTournamentResponse>();
        foreach (var t in tournaments.OrderBy(t => t.Date).ThenBy(t => t.Id))
        {
            byTournament.TryGetValue(t.Id, out var req);
            var registered = registeredIds.Contains(t.Id) || req?.EntryId is not null;
            var approved = registered || req?.Status == Approved;
            var item = new MyUpcomingTournamentResponse
            {
                TournamentId = t.Id,
                PublicSlug = t.IsPublic ? t.PublicSlug : null,
                Name = t.Name,
                Date = t.Date,
                Status = t.Status,
                HomeGameId = t.HomeGameId,
                HomeGameLabel = HomeGamePrimary.LabelFor(t.HomeGameIsPrimary, ownerPlans[t.OwnerId], t.HomeGameName, t.OwnerName),
                BuyInAmount = t.BuyInAmount,
                Registered = registered,
                RequestId = req?.Id,
                RequestStatus = req?.Status,
                WaitlistPosition = req?.Status == Waitlisted ? req.WaitlistPosition : null,
                LocationSummary = LocationSummary(t.District, t.City, t.State),
            };
            if (approved)
            {
                item.FullAddress = FullAddress(t.Street, t.Number, t.Complement, t.District, t.City, t.State, t.PostalCode, t.Location);
                item.MapUrl = MapUrl(t.Latitude, t.Longitude, item.FullAddress);
            }
            list.Add(item);
        }
        return list;
    }

    // ------------------------------------------------------------------ organizador

    /// <summary>Pedidos aguardando, reservas (sem inscrição) e lista de espera do torneio, com as vagas.</summary>
    public async Task<TournamentJoinOverviewResponse> ListForTournamentAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.AsNoTracking()
            .Where(t => t.Id == tournamentId)
            .Select(t => new { t.Id, t.MaxPlayers })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        var rows = await Requests()
            .Where(r => r.TournamentId == tournamentId && OpenStatuses.Contains(r.Status))
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(ct);
        var (taken, left) = await SpotsAsync(tournamentId, tournament.MaxPlayers, ct);
        return new TournamentJoinOverviewResponse
        {
            Pending = rows.Where(r => r.Status == Pending).Select(Map).ToList(),
            Reservations = rows.Where(r => r.Status == Approved && r.EntryId == null).OrderBy(r => r.DecidedAt).Select(Map).ToList(),
            Waitlist = rows.Where(r => r.Status == Waitlisted).OrderBy(r => r.WaitlistPosition).Select(Map).ToList(),
            MaxPlayers = tournament.MaxPlayers,
            SpotsTaken = taken,
            SpotsLeft = left,
        };
    }

    /// <summary>A fila "Reservas" (aprovadas sem inscrição), para o terminal do dealer também.</summary>
    public async Task<List<JoinRequestResponse>> ReservationsAsync(Guid tournamentId, CancellationToken ct = default)
    {
        if (!await _db.Tournaments.AnyAsync(t => t.Id == tournamentId, ct))
            throw new KeyNotFoundException();
        var rows = await Requests()
            .Where(r => r.TournamentId == tournamentId && r.Status == Approved && r.EntryId == null)
            .OrderBy(r => r.DecidedAt)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// Início: pedidos aguardando nos torneios dos home games que a conta GERENCIA (dona ou membro administrador; o Admin
    /// do sistema só nos que acessa, como o resto do Início), mais antigo primeiro.
    /// </summary>
    public async Task<List<InicioJoinRequest>> ListPendingForAccountAsync(Guid userId, bool isSystemAdmin, CancellationToken ct = default)
    {
        var homeGames = await _db.HomeGames.AsNoTracking()
            .Where(h => h.IsActive && (h.OwnerId == userId
                || h.Members.Any(m => m.UserId == userId && (m.IsAdmin || isSystemAdmin))))
            .Select(h => new { h.Id, h.Name, h.IsPrimary, h.OwnerId })
            .ToListAsync(ct);
        if (homeGames.Count == 0)
            return [];
        var ids = homeGames.Select(h => h.Id).ToList();

        var rows = await Requests()
            .Where(r => r.Status == Pending && ids.Contains(r.Tournament.HomeGameId)
                && r.Tournament.Status != Finished && r.Tournament.Status != CancelledTournament)
            .Select(r => new { Request = r, r.Tournament.Name, r.Tournament.HomeGameId })
            .OrderBy(x => x.Request.RequestedAt)
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];

        var ownerPlans = new Dictionary<Guid, string>();
        foreach (var ownerId in homeGames.Select(h => h.OwnerId).Distinct())
            ownerPlans[ownerId] = await _plans.GetUserEffectivePlanAsync(ownerId, ct);
        var byId = homeGames.ToDictionary(h => h.Id);

        return rows.Select(x =>
        {
            var hg = byId[x.HomeGameId];
            var item = new InicioJoinRequest
            {
                TournamentName = x.Name,
                HomeGameId = x.HomeGameId,
                HomeGameName = HomeGamePrimary.DisplayNameFor(hg.IsPrimary, ownerPlans[hg.OwnerId], hg.Name),
            };
            Fill(item, x.Request);
            return item;
        }).ToList();
    }

    /// <summary>
    /// Aprovar = reservar: garante o jogador da conta no clube (reaproveita o vinculado; senão cria com nome, apelido e
    /// e-mail da conta, já vinculado) e marca Approved — ou Waitlisted, com posição, se as vagas acabaram. Nada de
    /// inscrição nem transação. Notifica o interessado.
    /// </summary>
    public async Task<JoinRequestResponse> ApproveAsync(Guid tournamentId, Guid requestId, Guid? actorId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        var request = await Requests(track: true).FirstOrDefaultAsync(r => r.Id == requestId && r.TournamentId == tournamentId, ct)
            ?? throw new KeyNotFoundException(RequestNotFoundMessage);
        if (request.Status == Approved || request.Status == Waitlisted)
            return Map(request); // toque duplo
        if (request.Status != Pending)
            throw new DomainException(NotPendingMessage);
        if (tournament.Status == Finished || tournament.Status == CancelledTournament)
            throw new DomainException(OverMessage);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user is null || !user.IsActive || user.Email.EndsWith(PlayerLinkService.DeletedAccountEmailSuffix, StringComparison.Ordinal))
            throw new DomainException(InactiveAccountMessage);

        var now = DateTimeOffset.UtcNow;
        request.DecidedAt = now;
        request.DecidedBy = actorId;

        var (_, left) = await SpotsAsync(tournamentId, tournament.MaxPlayers, ct);
        var club = await ClubLabelAsync(tournament.HomeGameId, ct);
        if (left is <= 0)
        {
            var position = (await _db.TournamentJoinRequests
                .Where(r => r.TournamentId == tournamentId && r.Status == Waitlisted)
                .MaxAsync(r => (int?)r.WaitlistPosition, ct) ?? 0) + 1;
            request.Status = Waitlisted;
            request.WaitlistPosition = position;
            await _notifications.StageAsync(
                [user.Id],
                NotificationKind.JoinRequestWaitlisted,
                $"Lista de espera: {tournament.Name}",
                $"As vagas de {club} acabaram; você é o {position}º da lista de espera. Se alguém desistir, você sobe sozinho.",
                UpcomingLink,
                ct);
        }
        else
        {
            var person = await EnsurePersonAsync(tournament.HomeGameId, user, ct);
            request.Status = Approved;
            request.PersonId = person.Id;
            request.Person = person;
            await _notifications.StageAsync(
                [user.Id],
                NotificationKind.JoinRequestApproved,
                $"Vaga reservada: {tournament.Name}",
                $"Sua vaga em {club} está reservada. O endereço completo e o Como chegar estão em Meus torneios › Próximos.",
                UpcomingLink,
                ct);
        }

        await _db.SaveChangesAsync(ct);
        await _notifications.PushPendingAsync(ct);
        return Map(request);
    }

    /// <summary>Recusa um pedido aguardando (ou tira alguém da espera): a conta não pede de novo neste torneio.</summary>
    public async Task<JoinRequestResponse> DeclineAsync(Guid tournamentId, Guid requestId, Guid? actorId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        var request = await Requests(track: true).FirstOrDefaultAsync(r => r.Id == requestId && r.TournamentId == tournamentId, ct)
            ?? throw new KeyNotFoundException(RequestNotFoundMessage);
        if (request.Status == Declined)
            return Map(request);
        if (request.Status != Pending && request.Status != Waitlisted)
            throw new DomainException(NotPendingMessage);

        var wasWaitlisted = request.Status == Waitlisted;
        request.Status = Declined;
        request.WaitlistPosition = null;
        request.DecidedAt = DateTimeOffset.UtcNow;
        request.DecidedBy = actorId;
        if (wasWaitlisted)
            await RenumberWaitlistAsync(tournamentId, ct);

        await _notifications.StageAsync(
            [request.UserId],
            NotificationKind.JoinRequestDeclined,
            $"Pedido recusado: {tournament.Name}",
            "O organizador não aprovou o seu pedido de vaga neste torneio.",
            tournament.IsPublic && tournament.PublicSlug is not null ? TournamentPublicity.PublicPath(tournament.PublicSlug) : null,
            ct);

        await _db.SaveChangesAsync(ct);
        await _notifications.PushPendingAsync(ct);
        return Map(request);
    }

    /// <summary>
    /// "Remover reserva" (organizador): a reserva ainda na fila vira Cancelled (a conta pode pedir de novo) e a primeira
    /// da espera sobe. Reserva já inscrita não se remove por aqui (exclui-se a inscrição).
    /// </summary>
    public async Task<JoinRequestResponse> RemoveReservationAsync(Guid tournamentId, Guid requestId, Guid? actorId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        var request = await Requests(track: true).FirstOrDefaultAsync(r => r.Id == requestId && r.TournamentId == tournamentId, ct)
            ?? throw new KeyNotFoundException(RequestNotFoundMessage);
        if (request.Status == Cancelled)
            return Map(request);
        if (request.Status != Approved || request.EntryId is not null)
            throw new DomainException(NotReservationMessage);

        request.Status = Cancelled;
        request.DecidedAt = DateTimeOffset.UtcNow;
        request.DecidedBy = actorId;

        await _notifications.StageAsync(
            [request.UserId],
            NotificationKind.JoinRequestDeclined,
            $"Reserva removida: {tournament.Name}",
            "O organizador removeu a sua reserva neste torneio. Se quiser, peça a vaga de novo.",
            tournament.IsPublic && tournament.PublicSlug is not null ? TournamentPublicity.PublicPath(tournament.PublicSlug) : null,
            ct);
        await PromoteWaitlistAsync(tournamentId, ct);

        await _db.SaveChangesAsync(ct);
        await _notifications.PushPendingAsync(ct);
        return Map(request);
    }

    // ------------------------------------------------------------------ costura das mensagens (ITournamentRequestLookup)

    public async Task<bool> HasOpenRequestAsync(Guid tournamentId, Guid organizerUserId, Guid requesterUserId, CancellationToken ct = default)
    {
        var homeGameId = await _db.TournamentJoinRequests.AsNoTracking()
            .Where(r => r.TournamentId == tournamentId && r.UserId == requesterUserId && OpenStatuses.Contains(r.Status))
            .Select(r => (Guid?)r.Tournament.HomeGameId)
            .FirstOrDefaultAsync(ct);
        if (homeGameId is null)
            return false;
        return await HomeGameAccess.CanManageAsync(_db, homeGameId.Value, organizerUserId, false, ct);
    }

    /// <summary>
    /// O pedido aberto pelo id, para quem pediu (o organizador da conversa é quem criou o torneio, se gerencia o clube,
    /// senão o dono) ou para quem gerencia o clube (ele mesmo é o organizador da conversa). Null = 404.
    /// </summary>
    public async Task<TournamentRequestInfo?> FindOpenRequestAsync(Guid requestId, Guid callerUserId, CancellationToken ct = default)
    {
        var request = await _db.TournamentJoinRequests.AsNoTracking()
            .Where(r => r.Id == requestId && OpenStatuses.Contains(r.Status))
            .Select(r => new { r.TournamentId, r.UserId, r.Tournament.HomeGameId, r.Tournament.CreatedBy, OwnerId = r.Tournament.HomeGame.OwnerId })
            .FirstOrDefaultAsync(ct);
        if (request is null)
            return null;
        if (callerUserId == request.UserId)
        {
            var organizer = request.OwnerId;
            if (request.CreatedBy is { } creator && creator != request.UserId
                && await HomeGameAccess.CanManageAsync(_db, request.HomeGameId, creator, false, ct))
                organizer = creator;
            return new TournamentRequestInfo(request.TournamentId, organizer, request.UserId);
        }
        if (await HomeGameAccess.CanManageAsync(_db, request.HomeGameId, callerUserId, false, ct))
            return new TournamentRequestInfo(request.TournamentId, callerUserId, request.UserId);
        return null;
    }

    // ------------------------------------------------------------------ vagas e espera

    /// <summary>Inscrições + reservas sem inscrição; vagas que sobram (null sem limite).</summary>
    public async Task<(int Taken, int? Left)> SpotsAsync(Guid tournamentId, int? maxPlayers, CancellationToken ct)
    {
        var entries = await _db.TournamentEntries.CountAsync(e => e.TournamentId == tournamentId, ct);
        var reservations = await _db.TournamentJoinRequests
            .CountAsync(r => r.TournamentId == tournamentId && r.Status == Approved && r.EntryId == null, ct);
        var taken = entries + reservations;
        return (taken, maxPlayers is { } max ? Math.Max(0, max - taken) : null);
    }

    /// <summary>Uma reserva saiu: a primeira da espera vira reserva (jogador garantido no clube) e é avisada. Não salva.</summary>
    private async Task PromoteWaitlistAsync(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, ct);
        if (tournament is null || tournament.MaxPlayers is null)
            return;
        // As contas da espera já estão rastreadas se vieram por este contexto; lê de novo com tracking para mudar
        var next = await Requests(track: true)
            .Where(r => r.TournamentId == tournamentId && r.Status == Waitlisted)
            .OrderBy(r => r.WaitlistPosition).ThenBy(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (next is null)
            return;
        // A vaga que abriu ainda está "ocupada" pela reserva cancelada no contexto (não salvo): conta com ela liberada
        var (taken, _) = await SpotsAsync(tournamentId, tournament.MaxPlayers, ct);
        if (taken - 1 >= tournament.MaxPlayers)
            return;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == next.UserId, ct);
        if (user is null || !user.IsActive)
        {
            next.Status = Cancelled;
            next.WaitlistPosition = null;
            next.DecidedAt = DateTimeOffset.UtcNow;
            await RenumberWaitlistAsync(tournamentId, ct);
            return;
        }
        var person = await EnsurePersonAsync(tournament.HomeGameId, user, ct);
        next.Status = Approved;
        next.WaitlistPosition = null;
        next.PersonId = person.Id;
        next.Person = person;
        next.DecidedAt = DateTimeOffset.UtcNow;
        next.DecidedBy = null;
        await RenumberWaitlistAsync(tournamentId, ct);
        var club = await ClubLabelAsync(tournament.HomeGameId, ct);
        await _notifications.StageAsync(
            [user.Id],
            NotificationKind.JoinRequestPromoted,
            $"Sua vez: {tournament.Name}",
            $"Uma vaga abriu em {club} e a sua reserva está confirmada. O endereço e o Como chegar estão em Meus torneios › Próximos.",
            UpcomingLink,
            ct);
    }

    /// <summary>Posições 1..n da espera, pela ordem atual (posição, depois pedido). Não salva.</summary>
    private async Task RenumberWaitlistAsync(Guid tournamentId, CancellationToken ct)
    {
        var waiting = await Requests(track: true)
            .Where(r => r.TournamentId == tournamentId && r.Status == Waitlisted)
            .OrderBy(r => r.WaitlistPosition).ThenBy(r => r.RequestedAt)
            .ToListAsync(ct);
        var position = 1;
        foreach (var w in waiting)
            w.WaitlistPosition = position++;
    }

    /// <summary>
    /// O jogador da conta no clube: o já vinculado (<c>Person.UserId</c>), senão o cadastro sem conta com o e-mail dela
    /// (ganha o vínculo), senão um novo com nome, apelido e e-mail da conta (o e-mail fica de fora se outro jogador do
    /// clube já o tem). Inativo volta a ativo. Não salva (vai no SaveChanges de quem chama).
    /// </summary>
    private async Task<Person> EnsurePersonAsync(Guid homeGameId, User user, CancellationToken ct)
    {
        var linked = await _db.Persons.FirstOrDefaultAsync(p => p.HomeGameId == homeGameId && p.UserId == user.Id, ct);
        if (linked is not null)
        {
            linked.IsActive = true;
            return linked;
        }
        var normalized = PlayerLinkService.NormalizeEmail(user.Email);
        if (normalized is not null)
        {
            var byEmail = await _db.Persons
                .FirstOrDefaultAsync(p => p.HomeGameId == homeGameId && p.UserId == null && p.Email != null && p.Email.Trim().ToLower() == normalized, ct);
            if (byEmail is not null)
            {
                byEmail.UserId = user.Id;
                byEmail.User = user;
                byEmail.IsActive = true;
                return byEmail;
            }
        }
        var emailInUse = await PlayerLinkService.EmailInUseAsync(_db, homeGameId, user.Email, null, ct);
        var person = new Person
        {
            HomeGameId = homeGameId,
            Type = "Jogador",
            FullName = PlayerNames.NormalizeSpaces(user.FullName),
            Nickname = string.IsNullOrWhiteSpace(user.Nickname) ? null : PlayerNames.NormalizeSpaces(user.Nickname),
            Email = emailInUse ? null : user.Email,
            IsActive = true,
            UserId = user.Id,
            User = user,
        };
        _db.Persons.Add(person);
        return person;
    }

    // ------------------------------------------------------------------ apoio

    private IQueryable<TournamentJoinRequest> Requests(bool track = false)
    {
        var q = _db.TournamentJoinRequests
            .Include(r => r.User)
            .Include(r => r.Person).ThenInclude(p => p!.User)
            .AsQueryable();
        return track ? q : q.AsNoTracking();
    }

    private async Task<List<Guid>> ManagerIdsAsync(Guid homeGameId, CancellationToken ct)
    {
        var owner = await _db.HomeGames.AsNoTracking().Where(h => h.Id == homeGameId).Select(h => h.OwnerId).ToListAsync(ct);
        var admins = await _db.HomeGameMembers.AsNoTracking()
            .Where(m => m.HomeGameId == homeGameId && m.IsAdmin)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        return owner.Concat(admins).Distinct().ToList();
    }

    private async Task<string> ClubLabelAsync(Guid homeGameId, CancellationToken ct)
    {
        var hg = await _db.HomeGames.AsNoTracking()
            .Where(h => h.Id == homeGameId)
            .Select(h => new { h.Name, h.IsPrimary, h.OwnerId, OwnerName = h.Owner.FullName })
            .FirstOrDefaultAsync(ct);
        if (hg is null)
            return "o clube";
        var plan = await _plans.GetUserEffectivePlanAsync(hg.OwnerId, ct);
        return HomeGamePrimary.LabelFor(hg.IsPrimary, plan, hg.Name, hg.OwnerName);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is DbException { SqlState: "23505" };

    private static string DisplayName(string fullName, string? nickname) =>
        string.IsNullOrWhiteSpace(nickname) ? fullName : nickname;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    /// <summary>"Bairro, Cidade - UF" (o que houver); null sem nada.</summary>
    public static string? LocationSummary(string? district, string? city, string? state)
    {
        var cityState = string.IsNullOrWhiteSpace(city)
            ? (string.IsNullOrWhiteSpace(state) ? null : state.Trim())
            : string.IsNullOrWhiteSpace(state) ? city.Trim() : $"{city.Trim()} - {state.Trim()}";
        var parts = new[] { string.IsNullOrWhiteSpace(district) ? null : district.Trim(), cityState }
            .Where(s => !string.IsNullOrEmpty(s));
        var text = string.Join(", ", parts);
        return text.Length == 0 ? null : text;
    }

    /// <summary>"Rua X, 123 - apto 4 - Bairro, Cidade - UF, CEP 01310-100 (ponto de referência)"; null sem nada.</summary>
    public static string? FullAddress(
        string? street, string? number, string? complement, string? district, string? city, string? state, string? postalCode, string? reference)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(street))
        {
            var line = street.Trim();
            if (!string.IsNullOrWhiteSpace(number))
                line += $", {number.Trim()}";
            if (!string.IsNullOrWhiteSpace(complement))
                line += $" - {complement.Trim()}";
            parts.Add(line);
        }
        var summary = LocationSummary(district, city, state);
        if (summary is not null)
            parts.Add(summary);
        if (!string.IsNullOrWhiteSpace(postalCode) && postalCode.Length == 8)
            parts.Add($"CEP {postalCode[..5]}-{postalCode[5..]}");
        var text = string.Join(" - ", parts);
        if (!string.IsNullOrWhiteSpace(reference))
            text = text.Length == 0 ? reference.Trim() : $"{text} ({reference.Trim()})";
        return text.Length == 0 ? null : text;
    }

    /// <summary>Link de mapa: pelas coordenadas quando o clube foi geocodificado, senão pelo endereço; null sem nada.</summary>
    public static string? MapUrl(double? latitude, double? longitude, string? address)
    {
        if (latitude is { } lat && longitude is { } lng)
            return $"https://www.google.com/maps/search/?api=1&query={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return string.IsNullOrWhiteSpace(address) ? null : $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(address)}";
    }

    private static JoinRequestResponse Map(TournamentJoinRequest request)
    {
        var response = new JoinRequestResponse();
        Fill(response, request);
        return response;
    }

    private static void Fill(JoinRequestResponse target, TournamentJoinRequest request)
    {
        target.Id = request.Id;
        target.TournamentId = request.TournamentId;
        target.Status = request.Status;
        target.Message = request.Message;
        target.WaitlistPosition = request.Status == Waitlisted ? request.WaitlistPosition : null;
        target.RequestedAt = request.RequestedAt;
        target.DecidedAt = request.DecidedAt;
        target.UserId = request.UserId;
        target.UserName = DisplayName(request.User.FullName, request.User.Nickname);
        target.UserHandle = request.User.Handle;
        target.UserPhotoUrl = request.User.PhotoUrl;
        target.PersonId = request.PersonId;
        target.PersonName = request.Person is null ? null : EntryService.PlayerDisplayName(request.Person);
        target.PersonPhotoUrl = request.Person is null ? null : PlayerPhoto.Of(request.Person.User?.PhotoUrl ?? request.User.PhotoUrl, request.Person.PhotoUrl);
        target.HasEntry = request.EntryId is not null;
    }
}
