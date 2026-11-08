using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Vínculo pelo jogador (SPEC-20261108-1632, D1 do plano da rede do jogador): o organizador mostra o convite do torneio
/// (<see cref="TournamentInvite"/>, QR/link <c>/entrar-no-torneio/{code}</c>, 24 h, um ativo por torneio); quem lê vê os
/// inscritos ainda sem conta (<c>Person.UserId == null</c>) só com nome de exibição e foto, entra na conta e pede
/// "sou eu" (<see cref="PlayerLinkRequest"/>) — ou avisa que não está na lista. O organizador aprova (grava
/// <c>Person.UserId</c> e, com o cadastro sem e-mail, copia o e-mail da conta) ou recusa; o pedido vence em 24 h.
/// Mesma mecânica de aprovação do dealer por QR (<see cref="DealerAccessService"/>), sem token: a conta já está logada.
/// <para>
/// Notificações (SPEC-20261108-1635): <c>PlayerLinkRequested</c> para quem gerencia o home game ao criar o pedido;
/// <c>PlayerLinkAnswered</c> para a conta na aprovação/recusa. Histórico do torneio: <c>PlayerLinked</c>,
/// <c>PlayerLinkDeclined</c> e <c>PlayerUnlinked</c> (sem desfazer). Soltar o vínculo (<see cref="UnlinkPersonAsync"/>)
/// volta <c>Person.UserId</c> a null; a área do jogador deixa de mostrar aquele clube.
/// </para>
/// Quem chama confere o acesso: convite por quem acessa o torneio (TenantAccessFilter); lista/aprovar/recusar/soltar por
/// quem GERENCIA o home game (HomeGameAccess.CanManageAsync); claim por qualquer conta logada (token de dealer não chega).
/// </summary>
public class PlayerLinkRequestService
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromHours(24);
    public const int CodeLength = 8;
    /// <summary>Sem 0/O/1/I: o código também se digita à mão em "Vincular-me a um clube".</summary>
    public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    // Recusados/vencidos somem depois de 30 dias (os aprovados ficam: são a origem do vínculo ao soltar)
    private static readonly TimeSpan CleanupAge = TimeSpan.FromDays(30);

    public const string InvalidInviteMessage = "Convite inválido ou vencido. Peça um novo ao organizador.";
    public const string TournamentOverMessage = "O torneio já terminou.";
    public const string NotInListMarker = "não está na lista";

    private static readonly string Pending = nameof(PlayerLinkRequestStatus.Pending);
    private static readonly string Approved = nameof(PlayerLinkRequestStatus.Approved);
    private static readonly string Declined = nameof(PlayerLinkRequestStatus.Declined);
    private static readonly string Expired = nameof(PlayerLinkRequestStatus.Expired);

    private readonly IAppDbContext _db;
    private readonly PlanService _plans;
    private readonly NotificationService _notifications;

    public PlayerLinkRequestService(IAppDbContext db, PlanService plans, NotificationService notifications)
    {
        _db = db;
        _plans = plans;
        _notifications = notifications;
    }

    public static string JoinLink(string code) => $"/entrar-no-torneio/{code}";
    public static string DashboardLink(Guid tournamentId) => $"/tournaments/{tournamentId}";

    // ------------------------------------------------------------ convite (organizador)

    /// <summary>Gera o convite do torneio (renovar): o anterior deixa de valer (RevokedAt); os pedidos já feitos continuam.</summary>
    public async Task<TournamentInviteResponse> CreateInviteAsync(Guid tournamentId, Guid? userId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        if (IsOver(tournament.Status))
            throw new DomainException("O torneio já terminou: não dá para convidar jogadores.");

        await CleanupOldRequestsAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var active = await _db.TournamentInvites
            .Where(i => i.TournamentId == tournamentId && i.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var old in active)
            old.RevokedAt = now;

        var invite = new TournamentInvite
        {
            TournamentId = tournamentId,
            Code = await NewCodeAsync(ct),
            ExpiresAt = now + InviteLifetime,
            CreatedBy = userId,
        };
        _db.TournamentInvites.Add(invite);
        await _db.SaveChangesAsync(ct);
        return await MapInviteAsync(invite, ct);
    }

    /// <summary>Convite ativo do torneio (para reexibir o QR), ou null.</summary>
    public async Task<TournamentInviteResponse?> GetActiveInviteAsync(Guid tournamentId, CancellationToken ct = default)
    {
        if (!await _db.Tournaments.AnyAsync(t => t.Id == tournamentId, ct))
            throw new KeyNotFoundException();
        var now = DateTimeOffset.UtcNow;
        var invite = await _db.TournamentInvites.AsNoTracking()
            .Where(i => i.TournamentId == tournamentId && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return invite is null ? null : await MapInviteAsync(invite, ct);
    }

    // ------------------------------------------------------------ página do convite (jogador)

    /// <summary>
    /// O que quem lê o convite vê, sem login: torneio, clube e os inscritos ainda sem conta — nome de exibição e foto do
    /// cadastro, nada de e-mail/WhatsApp. Convite revogado/vencido/inexistente → 400 com a mensagem.
    /// </summary>
    public async Task<InviteInfoResponse> GetInviteInfoAsync(string? code, CancellationToken ct = default)
    {
        var (invite, tournament) = await ResolveInviteAsync(code, ct);
        var homeGame = await _db.HomeGames.AsNoTracking()
            .Where(h => h.Id == tournament.HomeGameId)
            .Select(h => new { h.Id, h.Name, h.IsPrimary, h.OwnerId, OwnerName = h.Owner.FullName })
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException(InvalidInviteMessage);
        var ownerPlan = await _plans.GetUserEffectivePlanAsync(homeGame.OwnerId, ct);

        var players = await _db.TournamentEntries.AsNoTracking()
            .Where(e => e.TournamentId == tournament.Id && e.Person.UserId == null)
            .Select(e => new { e.Person.Id, e.Person.FullName, e.Person.Nickname, e.Person.PhotoUrl })
            .ToListAsync(ct);

        return new InviteInfoResponse
        {
            TournamentId = tournament.Id,
            TournamentName = tournament.Name,
            Date = tournament.Date,
            Status = tournament.Status,
            ClubName = HomeGamePrimary.LabelFor(homeGame.IsPrimary, ownerPlan, homeGame.Name, homeGame.OwnerName),
            ExpiresAt = invite.ExpiresAt,
            Players = players
                .GroupBy(p => p.Id)
                .Select(g => g.First())
                .Select(p => new InvitePlayerResponse
                {
                    PersonId = p.Id,
                    DisplayName = string.IsNullOrWhiteSpace(p.Nickname) ? p.FullName : p.Nickname!,
                    PhotoUrl = p.PhotoUrl,
                })
                .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
        };
    }

    /// <summary>O último pedido desta conta no torneio do convite (qualquer estado) e se ela já é um inscrito vinculado.</summary>
    public async Task<MyLinkRequestResponse> GetMyRequestAsync(string? code, Guid userId, CancellationToken ct = default)
    {
        var (_, tournament) = await ResolveInviteAsync(code, ct);
        await ExpireStaleAsync(tournament.Id, ct);

        var linkedName = await _db.TournamentEntries.AsNoTracking()
            .Where(e => e.TournamentId == tournament.Id && e.Person.UserId == userId)
            .Select(e => new { e.Person.FullName, e.Person.Nickname })
            .FirstOrDefaultAsync(ct);

        var request = await Requests()
            .Where(r => r.TournamentId == tournament.Id && r.UserId == userId)
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct);

        return new MyLinkRequestResponse
        {
            TournamentId = tournament.Id,
            LinkedPlayerName = linkedName is null ? null : DisplayName(linkedName.FullName, linkedName.Nickname),
            Request = request is null ? null : Map(request, includeEmail: false),
        };
    }

    /// <summary>
    /// "Sou eu" (<paramref name="request"/>.PersonId) ou "não estou na lista" (<c>NotInList</c>). Um pedido pendente por
    /// conta e torneio (409 se já houver); jogador que já tem conta → 409; conta que já é um inscrito vinculado → 409.
    /// Avisa quem gerencia o home game (PlayerLinkRequested).
    /// </summary>
    public async Task<PlayerLinkRequestResponse> ClaimAsync(
        string? code, Guid userId, ClaimInviteRequest request, CancellationToken ct = default)
    {
        var (invite, tournament) = await ResolveInviteAsync(code, ct);
        if (IsOver(tournament.Status))
            throw new DomainException(TournamentOverMessage);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new DomainException("Conta não encontrada. Entre de novo.");

        var now = DateTimeOffset.UtcNow;
        await ExpireStaleAsync(tournament.Id, ct);

        var linkedName = await _db.TournamentEntries.AsNoTracking()
            .Where(e => e.TournamentId == tournament.Id && e.Person.UserId == userId)
            .Select(e => new { e.Person.FullName, e.Person.Nickname })
            .FirstOrDefaultAsync(ct);
        if (linkedName is not null)
            throw new InvalidOperationException($"Você já está neste torneio como {DisplayName(linkedName.FullName, linkedName.Nickname)}.");

        if (await _db.PlayerLinkRequests.AnyAsync(r => r.TournamentId == tournament.Id && r.UserId == userId && r.Status == Pending, ct))
            throw new InvalidOperationException("Você já tem um pedido aguardando o organizador neste torneio.");

        Person? person = null;
        if (!request.NotInList)
        {
            if (request.PersonId is not { } personId)
                throw new DomainException("Escolha quem você é na lista ou toque em \"Não estou na lista\".");
            person = await _db.TournamentEntries.AsNoTracking()
                .Where(e => e.TournamentId == tournament.Id && e.PersonId == personId)
                .Select(e => e.Person)
                .FirstOrDefaultAsync(ct)
                ?? throw new DomainException("Este jogador não está na lista do torneio.");
            if (person.UserId is not null)
                throw new InvalidOperationException("Este jogador já tem uma conta vinculada. Se for você, fale com o organizador.");
        }

        var created = new PlayerLinkRequest
        {
            TournamentId = tournament.Id,
            PersonId = person?.Id,
            UserId = userId,
            InviteId = invite.Id,
            Status = Pending,
            Message = person is null ? NotInListMarker : null,
            RequestedAt = now,
            ExpiresAt = now + RequestLifetime,
        };
        _db.PlayerLinkRequests.Add(created);

        var managers = await ManagerIdsAsync(tournament.HomeGameId, ct);
        var who = DisplayName(user.FullName, user.Nickname);
        var body = person is null
            ? $"{who} quer entrar no torneio, não está na lista. Cadastre o jogador com o e-mail {user.Email} para vincular."
            : $"{who} diz que é {EntryService.PlayerDisplayName(person)}. Aprove ou recuse no painel.";
        await _notifications.StageAsync(
            managers.Where(m => m != userId),
            NotificationKind.PlayerLinkRequested,
            $"Pedido de vínculo: {tournament.Name}",
            body,
            DashboardLink(tournament.Id),
            ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Dois toques no "Sou eu" ao mesmo tempo: o índice ux_player_link_requests_pending_user deixa entrar um só
            throw new InvalidOperationException("Você já tem um pedido aguardando o organizador neste torneio.");
        }
        await _notifications.PushPendingAsync(ct);

        created.User = user;
        created.Person = person;
        return Map(created, includeEmail: false);
    }

    // ------------------------------------------------------------ organizador

    /// <summary>Pedidos pendentes do torneio (mais antigo primeiro), com o e-mail da conta para quem gerencia.</summary>
    public async Task<List<PlayerLinkRequestResponse>> ListPendingAsync(Guid tournamentId, CancellationToken ct = default)
    {
        if (!await _db.Tournaments.AnyAsync(t => t.Id == tournamentId, ct))
            throw new KeyNotFoundException();
        await ExpireStaleAsync(tournamentId, ct);
        var rows = await Requests()
            .Where(r => r.TournamentId == tournamentId && r.Status == Pending)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(ct);
        return rows.Select(r => Map(r, includeEmail: true)).ToList();
    }

    /// <summary>
    /// Início (SPEC-20261108-1632): pedidos pendentes dos torneios dos home games que a conta GERENCIA (dona ou membro
    /// administrador; o Admin do sistema só nos que acessa, como o resto do Início), mais antigo primeiro.
    /// </summary>
    public async Task<List<InicioLinkRequest>> ListPendingForAccountAsync(Guid userId, bool isSystemAdmin, CancellationToken ct = default)
    {
        var homeGames = await _db.HomeGames.AsNoTracking()
            .Where(h => h.IsActive && (h.OwnerId == userId
                || h.Members.Any(m => m.UserId == userId && (m.IsAdmin || isSystemAdmin))))
            .Select(h => new { h.Id, h.Name, h.IsPrimary, h.OwnerId })
            .ToListAsync(ct);
        if (homeGames.Count == 0)
            return [];
        var ids = homeGames.Select(h => h.Id).ToList();

        var now = DateTimeOffset.UtcNow;
        await _db.PlayerLinkRequests
            .Where(r => r.Status == Pending && r.ExpiresAt <= now && _db.Tournaments.Any(t => t.Id == r.TournamentId && ids.Contains(t.HomeGameId)))
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, Expired)
                .SetProperty(r => r.DecidedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);

        var rows = await Requests()
            .Where(r => r.Status == Pending)
            .Join(_db.Tournaments.AsNoTracking().Where(t => ids.Contains(t.HomeGameId)),
                r => r.TournamentId, t => t.Id,
                (r, t) => new { Request = r, t.Name, t.HomeGameId })
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
            var item = new InicioLinkRequest
            {
                TournamentName = x.Name,
                HomeGameId = x.HomeGameId,
                HomeGameName = HomeGamePrimary.DisplayNameFor(hg.IsPrimary, ownerPlans[hg.OwnerId], hg.Name),
            };
            Fill(item, x.Request, includeEmail: true);
            return item;
        }).ToList();
    }

    /// <summary>
    /// Aprova: grava <c>Person.UserId</c>; cadastro sem e-mail recebe o e-mail da conta (a não ser que outro jogador do
    /// home game já o tenha); com outro e-mail, mantém e registra. Outros pedidos pendentes para o MESMO jogador são
    /// recusados automaticamente. Atividade <c>PlayerLinked</c> e notificação <c>PlayerLinkAnswered</c>. No pedido
    /// "não estou na lista" o organizador indica o jogador em <paramref name="personIdOverride"/> (cadastrado, sem conta).
    /// </summary>
    public async Task<PlayerLinkRequestResponse> ApproveAsync(
        Guid tournamentId, Guid requestId, Guid? actorId, Guid? personIdOverride, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        var request = await Requests(track: true).FirstOrDefaultAsync(r => r.Id == requestId && r.TournamentId == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        if (request.Status == Approved)
            return Map(request, includeEmail: true); // toque duplo
        if (request.Status != Pending)
            throw new DomainException(AlreadyDecidedMessage(request.Status));

        var now = DateTimeOffset.UtcNow;
        if (request.ExpiresAt <= now)
        {
            request.Status = Expired;
            request.DecidedAt = now;
            await _db.SaveChangesAsync(ct);
            throw new DomainException("Este pedido venceu (mais de 24 horas sem resposta). Peça ao jogador para ler o convite de novo.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
            ?? throw new DomainException("A conta que pediu o vínculo não existe mais.");
        if (!user.IsActive || user.Email.EndsWith(PlayerLinkService.DeletedAccountEmailSuffix, StringComparison.Ordinal))
            throw new DomainException("A conta que pediu o vínculo foi desativada ou excluída.");

        var personId = request.PersonId ?? personIdOverride
            ?? throw new DomainException("Este pedido não indica um jogador: cadastre o jogador com o e-mail da conta (o vínculo é automático) ou indique quem é ele ao aprovar.");
        var person = await _db.Persons.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == personId && p.HomeGameId == tournament.HomeGameId, ct)
            ?? throw new DomainException("Jogador não encontrado neste clube.");
        if (person.UserId is not null)
            throw new InvalidOperationException(person.UserId == user.Id
                ? "Este jogador já está vinculado a esta conta."
                : "Este jogador já foi vinculado a outra conta. Recuse o pedido ou solte o vínculo antes.");

        // Vínculo + e-mail
        person.UserId = user.Id;
        person.User = user;
        string suffix;
        if (string.IsNullOrWhiteSpace(person.Email))
        {
            if (await PlayerLinkService.EmailInUseAsync(_db, person.HomeGameId, user.Email, person.Id, ct))
            {
                suffix = " · o e-mail da conta já está em outro jogador do clube (cadastro continua sem e-mail)";
                request.Message = "e-mail da conta já usado por outro jogador; cadastro sem e-mail";
            }
            else
            {
                person.Email = user.Email;
                request.EmailCopied = true;
                suffix = " · e-mail da conta copiado para o cadastro";
                request.Message = "e-mail da conta copiado para o cadastro";
            }
        }
        else
        {
            suffix = $" · o cadastro já tinha o e-mail {person.Email} (mantido)";
            request.Message = $"cadastro já tinha outro e-mail ({person.Email}), mantido";
        }
        request.PersonId = person.Id;
        request.Person = person;
        request.Status = Approved;
        request.DecidedAt = now;
        request.DecidedBy = actorId;

        var who = DisplayName(user.FullName, user.Nickname);
        var playerName = EntryService.PlayerDisplayName(person);
        var entryId = await _db.TournamentEntries.AsNoTracking()
            .Where(e => e.TournamentId == tournamentId && e.PersonId == person.Id)
            .OrderBy(e => e.RegisteredAt)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);
        _db.TournamentActivities.Add(new TournamentActivity
        {
            TournamentId = tournamentId,
            EntryId = entryId,
            Type = nameof(TournamentActivityType.PlayerLinked),
            Description = $"{playerName} foi vinculado à conta de {who}{suffix}",
            CreatedBy = actorId,
        });

        // Outros pedidos pendentes para o mesmo jogador (em qualquer torneio): o jogador já ficou com uma conta
        var others = await _db.PlayerLinkRequests
            .Where(r => r.PersonId == person.Id && r.Status == Pending && r.Id != request.Id)
            .ToListAsync(ct);
        foreach (var other in others)
        {
            other.Status = Declined;
            other.DecidedAt = now;
            other.DecidedBy = actorId;
            other.Message = "o jogador já foi vinculado a outra conta";
            await _notifications.StageAsync(
                [other.UserId],
                NotificationKind.PlayerLinkAnswered,
                $"Pedido de vínculo recusado: {tournament.Name}",
                $"{playerName} já foi vinculado a outra conta. Se quiser, peça de novo escolhendo outro jogador.",
                await JoinLinkAsync(tournamentId, ct),
                ct);
        }

        var club = await ClubLabelAsync(tournament.HomeGameId, ct);
        await _notifications.StageAsync(
            [user.Id],
            NotificationKind.PlayerLinkAnswered,
            $"Vínculo aprovado: {tournament.Name}",
            $"Você é {playerName} em {club}. Seus torneios já aparecem em Meus torneios.",
            TournamentNotifier.MyTournamentLink(tournamentId),
            ct);

        await _db.SaveChangesAsync(ct);
        await _notifications.PushPendingAsync(ct);
        return Map(request, includeEmail: true);
    }

    /// <summary>Recusa (ou dispensa o "não estou na lista"): o jogador pode pedir de novo escolhendo outro inscrito.</summary>
    public async Task<PlayerLinkRequestResponse> DeclineAsync(
        Guid tournamentId, Guid requestId, Guid? actorId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        var request = await Requests(track: true).FirstOrDefaultAsync(r => r.Id == requestId && r.TournamentId == tournamentId, ct)
            ?? throw new KeyNotFoundException();
        if (request.Status == Declined)
            return Map(request, includeEmail: true);
        if (request.Status != Pending)
            throw new DomainException(request.Status == Approved
                ? "Este pedido já foi aprovado. Para desfazer, solte o vínculo na ficha do jogador."
                : AlreadyDecidedMessage(request.Status));

        var now = DateTimeOffset.UtcNow;
        request.Status = Declined;
        request.DecidedAt = now;
        request.DecidedBy = actorId;

        var who = DisplayName(request.User.FullName, request.User.Nickname);
        var notInList = request.PersonId is null;
        var playerName = notInList ? null : EntryService.PlayerDisplayName(request.Person);
        var entryId = notInList
            ? null
            : await _db.TournamentEntries.AsNoTracking()
                .Where(e => e.TournamentId == tournamentId && e.PersonId == request.PersonId)
                .OrderBy(e => e.RegisteredAt)
                .Select(e => (Guid?)e.Id)
                .FirstOrDefaultAsync(ct);
        _db.TournamentActivities.Add(new TournamentActivity
        {
            TournamentId = tournamentId,
            EntryId = entryId,
            Type = nameof(TournamentActivityType.PlayerLinkDeclined),
            Description = notInList
                ? $"Aviso de {who} (não está na lista) dispensado"
                : $"Pedido de vínculo de {who} para {playerName} recusado",
            CreatedBy = actorId,
        });

        await _notifications.StageAsync(
            [request.UserId],
            NotificationKind.PlayerLinkAnswered,
            $"Pedido de vínculo recusado: {tournament.Name}",
            notInList
                ? "O organizador viu seu aviso de que você não estava na lista. Se ele cadastrar você, peça o vínculo de novo pelo convite."
                : $"O organizador não confirmou que você é {playerName}. Se quiser, peça de novo escolhendo outro jogador.",
            await JoinLinkAsync(tournamentId, ct),
            ct);

        await _db.SaveChangesAsync(ct);
        await _notifications.PushPendingAsync(ct);
        return Map(request, includeEmail: true);
    }

    /// <summary>
    /// Solta o vínculo do jogador (<c>Person.UserId = null</c>): a área do jogador deixa de mostrar aquele clube e a foto
    /// volta a ser a do cadastro. Atividade <c>PlayerUnlinked</c> no torneio do pedido aprovado que originou o vínculo (se
    /// o vínculo veio pelo e-mail ou por "vincular membro", não há torneio: fica só a mudança). Devolve a pessoa.
    /// Null = pessoa inexistente.
    /// </summary>
    public async Task<Person?> UnlinkPersonAsync(Guid personId, Guid? actorId, CancellationToken ct = default)
    {
        var person = await _db.Persons.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == personId, ct);
        if (person is null)
            return null;
        if (person.UserId is not { } userId)
            throw new DomainException("Este jogador não está vinculado a nenhuma conta.");

        var who = person.User is null ? "uma conta" : DisplayName(person.User.FullName, person.User.Nickname);
        var origin = await _db.PlayerLinkRequests.AsNoTracking()
            .Where(r => r.PersonId == personId && r.UserId == userId && r.Status == Approved)
            .OrderByDescending(r => r.DecidedAt)
            .Select(r => new { r.TournamentId })
            .FirstOrDefaultAsync(ct);
        if (origin is not null)
        {
            var entryId = await _db.TournamentEntries.AsNoTracking()
                .Where(e => e.TournamentId == origin.TournamentId && e.PersonId == personId)
                .OrderBy(e => e.RegisteredAt)
                .Select(e => (Guid?)e.Id)
                .FirstOrDefaultAsync(ct);
            _db.TournamentActivities.Add(new TournamentActivity
            {
                TournamentId = origin.TournamentId,
                EntryId = entryId,
                Type = nameof(TournamentActivityType.PlayerUnlinked),
                Description = $"Vínculo de {EntryService.PlayerDisplayName(person)} com a conta de {who} desfeito",
                CreatedBy = actorId,
            });
        }

        person.UserId = null;
        person.User = null;
        await _db.SaveChangesAsync(ct);
        return person;
    }

    /// <summary>Jogadores vinculados a uma conta, em qualquer home game (ficha da Administração).</summary>
    public Task<List<AdminUserLinkedPlayer>> LinkedPlayersAsync(Guid userId, CancellationToken ct = default) =>
        _db.Persons.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.HomeGame.Name)
            .ThenBy(p => p.FullName)
            .Select(p => new AdminUserLinkedPlayer
            {
                PersonId = p.Id,
                FullName = p.FullName,
                Nickname = p.Nickname,
                Email = p.Email,
                PhotoUrl = p.PhotoUrl,
                HomeGameId = p.HomeGameId,
                HomeGameName = p.HomeGame.Name,
            })
            .ToListAsync(ct);

    // ------------------------------------------------------------------ apoio

    private IQueryable<PlayerLinkRequest> Requests(bool track = false)
    {
        var query = _db.PlayerLinkRequests.Include(r => r.User).Include(r => r.Person);
        return track ? query : query.AsNoTracking();
    }

    /// <summary>Código normalizado (maiúsculas, sem espaços/hífens) → convite ativo e o torneio (não excluído), ou 400.</summary>
    private async Task<(TournamentInvite Invite, Tournament Tournament)> ResolveInviteAsync(string? code, CancellationToken ct)
    {
        var normalized = NormalizeCode(code);
        if (normalized is null)
            throw new DomainException(InvalidInviteMessage);
        var now = DateTimeOffset.UtcNow;
        var invite = await _db.TournamentInvites.AsNoTracking().FirstOrDefaultAsync(i => i.Code == normalized, ct);
        if (invite is null || invite.RevokedAt is not null || invite.ExpiresAt <= now)
            throw new DomainException(InvalidInviteMessage);
        var tournament = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == invite.TournamentId, ct)
            ?? throw new DomainException(InvalidInviteMessage);
        return (invite, tournament);
    }

    /// <summary>Maiúsculas, sem espaços nem hífens; só o alfabeto do código com 8 caracteres; senão null.</summary>
    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 32)
            return null;
        var cleaned = new string(code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        if (cleaned.Length != CodeLength)
            return null;
        foreach (var c in cleaned)
            if (!CodeAlphabet.Contains(c))
                return null;
        return cleaned;
    }

    private async Task<string> NewCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var chars = new char[CodeLength];
            for (var i = 0; i < CodeLength; i++)
                chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            var code = new string(chars);
            if (!await _db.TournamentInvites.AnyAsync(i => i.Code == code, ct))
                return code;
        }
        throw new DomainException("Não foi possível gerar o código do convite. Tente de novo.");
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

    /// <summary>Link da página do convite ativo do torneio (para o jogador pedir de novo), ou null sem convite ativo.</summary>
    private async Task<string?> JoinLinkAsync(Guid tournamentId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var code = await _db.TournamentInvites.AsNoTracking()
            .Where(i => i.TournamentId == tournamentId && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => i.Code)
            .FirstOrDefaultAsync(ct);
        return code is null ? null : JoinLink(code);
    }

    private async Task ExpireStaleAsync(Guid tournamentId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.PlayerLinkRequests
            .Where(r => r.TournamentId == tournamentId && r.Status == Pending && r.ExpiresAt <= now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, Expired)
                .SetProperty(r => r.DecidedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);
    }

    private async Task CleanupOldRequestsAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - CleanupAge;
        await _db.PlayerLinkRequests
            .Where(r => (r.Status == Declined || r.Status == Expired) && (r.DecidedAt ?? r.RequestedAt) < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private async Task<TournamentInviteResponse> MapInviteAsync(TournamentInvite invite, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = await _db.PlayerLinkRequests.CountAsync(
            r => r.TournamentId == invite.TournamentId && r.Status == Pending && r.ExpiresAt > now, ct);
        return new TournamentInviteResponse
        {
            Id = invite.Id,
            Code = invite.Code,
            ExpiresAt = invite.ExpiresAt,
            CreatedAt = invite.CreatedAt,
            PendingCount = pending,
        };
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is DbException { SqlState: "23505" };

    private static string AlreadyDecidedMessage(string status) => status == Expired
        ? "Este pedido venceu. Peça ao jogador para ler o convite de novo."
        : "Este pedido já foi recusado. O jogador pode pedir de novo pelo convite.";

    private static bool IsOver(string status) =>
        status == nameof(TournamentStatus.Finished) || status == nameof(TournamentStatus.Cancelled);

    private static string DisplayName(string fullName, string? nickname) =>
        string.IsNullOrWhiteSpace(nickname) ? fullName : nickname;

    private static PlayerLinkRequestResponse Map(PlayerLinkRequest request, bool includeEmail)
    {
        var response = new PlayerLinkRequestResponse();
        Fill(response, request, includeEmail);
        return response;
    }

    private static void Fill(PlayerLinkRequestResponse target, PlayerLinkRequest request, bool includeEmail)
    {
        target.Id = request.Id;
        target.TournamentId = request.TournamentId;
        target.Status = request.Status;
        target.RequestedAt = request.RequestedAt;
        target.ExpiresAt = request.ExpiresAt;
        target.DecidedAt = request.DecidedAt;
        target.NotInList = request.PersonId is null;
        target.Message = request.Message;
        target.EmailCopied = request.EmailCopied;
        target.UserId = request.UserId;
        target.UserName = request.User.FullName;
        target.UserNickname = request.User.Nickname;
        target.UserEmail = includeEmail ? request.User.Email : null;
        target.UserPhotoUrl = request.User.PhotoUrl;
        target.PersonId = request.PersonId;
        target.PersonName = request.Person is null ? null : EntryService.PlayerDisplayName(request.Person);
        // Antes da aprovação o jogador não tem conta: a foto exibida é a do cadastro; depois, a da conta vale (PlayerPhoto)
        target.PersonPhotoUrl = request.Person is null ? null : PlayerPhoto.Of(request.User.PhotoUrl is not null && request.Person.UserId == request.UserId ? request.User.PhotoUrl : null, request.Person.PhotoUrl);
    }
}
