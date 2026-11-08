using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Geo;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Perfil público do jogador e privacidade (SPEC-20261108-1636, plano §3, D4 e D12).
/// <para>
/// <b>Leitura</b> (<c>GET players/{handle}</c>): a dona vê tudo; terceiro vê conforme <c>ProfileVisibility</c> —
/// <c>Everyone</c> completo, <c>Friends</c> completo só para amigo (<see cref="IFriendshipLookup"/>; até a
/// SPEC-20261108-1637 ninguém é amigo), <c>Nobody</c> só foto e apelido (<c>isPrivate</c>). Bloqueado em qualquer
/// sentido → null (404, como handle inexistente: não diz que a conta existe). <c>?preview=stranger</c> aplica a regra
/// como para um desconhecido (a pré-visualização "como os outros veem" do Meu perfil).
/// </para>
/// <para>
/// <b>O que NUNCA sai para terceiros</b>: nome completo (o nome de exibição é o apelido ou só o primeiro nome), e-mail,
/// WhatsApp, documento e qualquer dinheiro (prêmio, buy-in, saldo). Os números da temporada saem só das inscrições
/// vinculadas (<c>Person.UserId == conta</c>) em torneios finalizados do ano corrente; "clubes em comum" são os home
/// games em que os DOIS (quem vê e o perfil) são dono, membro ativo ou têm inscrição vinculada.
/// </para>
/// <para>
/// <b>Escrita</b> (<c>PUT me/public-profile</c>, <c>PUT me/privacy</c>): só a própria conta, com AuditLog sem dados
/// pessoais (nomes dos campos), como o resto do Meu perfil.
/// </para>
/// </summary>
public class PlayerProfileService
{
    public const string PublicProfileUpdatedAction = "PublicProfileUpdated";
    public const string PrivacyUpdatedAction = "PrivacyUpdated";

    public const string ProfileNotFoundMessage = "Perfil não encontrado.";
    public const string CityTooLongMessage = "A cidade deve ter no máximo 100 caracteres.";
    public const string BioTooLongMessage = "A bio deve ter no máximo 200 caracteres.";
    public const string InvalidVisibilityMessage = "Visibilidade do perfil inválida.";
    public const string InvalidFriendRequestsMessage = "Opção de pedidos de amizade inválida.";
    public const string InvalidMessagesMessage = "Opção de mensagens inválida.";

    /// <summary>Valor de <c>?preview=</c> que aplica a regra de desconhecido.</summary>
    public const string StrangerPreview = "stranger";

    /// <summary>Rankings públicos por clube em comum (os primeiros pelo nome).</summary>
    public const int MaxRankingsPerHomeGame = 3;

    public const int CityMaxLength = 100;
    public const int BioMaxLength = 200;

    /// <summary>Ano da temporada pelo horário de Brasília (sem horário de verão desde 2019).</summary>
    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    private static readonly string[] ItmEntryStatuses =
    [
        nameof(EntryStatus.Awarded),
        nameof(EntryStatus.PaidOut),
    ];

    private readonly IAppDbContext _db;
    private readonly IFriendshipLookup _friends;
    private readonly PlanService _plans;
    private readonly AuthService _auth;

    public PlayerProfileService(IAppDbContext db, IFriendshipLookup friends, PlanService plans, AuthService auth)
    {
        _db = db;
        _friends = friends;
        _plans = plans;
        _auth = auth;
    }

    /// <summary>Nome que aparece para terceiros: o apelido; sem apelido, só o primeiro nome.</summary>
    public static string DisplayNameOf(string? nickname, string? fullName)
    {
        var nick = (nickname ?? string.Empty).Trim();
        if (nick.Length > 0)
            return nick;
        var name = (fullName ?? string.Empty).Trim();
        var space = name.IndexOf(' ');
        return space > 0 ? name[..space] : name;
    }

    // ------------------------------------------------------------------ leitura

    /// <summary>
    /// O perfil de <paramref name="handle"/> para a conta <paramref name="viewerId"/>. Null = não existe, não está ativa
    /// e confirmada, ou há bloqueio entre as duas (404 em todos os casos).
    /// </summary>
    public async Task<PlayerProfileResponse?> GetByHandleAsync(
        string handle, Guid viewerId, bool previewAsStranger, CancellationToken ct = default)
    {
        var key = (handle ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();
        if (key.Length == 0 || key.Length > HandleGenerator.MaxLength)
            return null;

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Handle == key && u.IsActive && u.EmailConfirmedAt != null)
            .Select(u => new
            {
                u.Id, u.Handle, u.Nickname, u.FullName, u.PhotoUrl, u.City, u.State, u.Bio, u.ProfileVisibility,
            })
            .FirstOrDefaultAsync(ct);
        if (user is null)
            return null;

        var isOwner = user.Id == viewerId;
        if (!isOwner && await _friends.IsBlockedAsync(viewerId, user.Id, ct))
            return null;

        // Como quem vê é tratado: dona (tudo), amigo ou desconhecido
        var asStranger = previewAsStranger || !isOwner;
        var canSeeFull = !asStranger || user.ProfileVisibility switch
        {
            nameof(ProfileVisibility.Everyone) => true,
            nameof(ProfileVisibility.Friends) => !previewAsStranger && await _friends.AreFriendsAsync(viewerId, user.Id, ct),
            _ => false,
        };

        var response = new PlayerProfileResponse
        {
            Id = user.Id,
            Handle = user.Handle,
            DisplayName = DisplayNameOf(user.Nickname, user.FullName),
            PhotoUrl = user.PhotoUrl,
            IsOwner = isOwner && !previewAsStranger,
            IsPreview = isOwner && previewAsStranger,
            IsPrivate = !canSeeFull,
        };
        if (!canSeeFull)
            return response;

        response.City = user.City;
        response.State = user.State;
        response.Bio = user.Bio;
        response.CommonHomeGames = await CommonHomeGamesAsync(user.Id, viewerId, ct);
        response.Season = await SeasonStatsAsync(user.Id, ct);
        // Conquistas (SPEC futura) e amigos (SPEC-20261108-1637): placeholders
        response.Achievements = null;
        response.FriendCount = null;
        return response;
    }

    /// <summary>
    /// Home games ativos em que os dois participam (dono, membro ativo ou inscrição vinculada em torneio não cancelado),
    /// com o rótulo da área do jogador e os rankings ativos com página pública. Quem vê o próprio perfil recebe todos
    /// os seus clubes (em comum consigo).
    /// </summary>
    private async Task<List<PlayerProfileHomeGame>> CommonHomeGamesAsync(Guid targetId, Guid viewerId, CancellationToken ct)
    {
        var target = await HomeGameIdsAsync(targetId, ct);
        var viewer = targetId == viewerId ? target : await HomeGameIdsAsync(viewerId, ct);
        var common = target.Intersect(viewer).ToList();
        if (common.Count == 0)
            return [];

        var homeGames = await _db.HomeGames.AsNoTracking()
            .Where(h => common.Contains(h.Id))
            .Select(h => new
            {
                h.Id,
                h.Name,
                h.IsPrimary,
                h.OwnerId,
                OwnerName = h.Owner.FullName,
                Rankings = h.Rankings
                    .Where(r => r.IsActive && r.Slug != null)
                    .OrderBy(r => r.Name)
                    .Take(MaxRankingsPerHomeGame)
                    .Select(r => new PlayerProfileRanking { Name = r.Name, Slug = r.Slug! })
                    .ToList(),
            })
            .ToListAsync(ct);

        var result = new List<PlayerProfileHomeGame>();
        foreach (var h in homeGames.OrderBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            // O mesmo rótulo de "Meus torneios" (MePlayerService.LabelFor): nome do clube; no principal de dono Mesa,
            // "Torneios de <organizador>" (ou "Seus torneios" quando o organizador é quem vê)
            var ownerPlan = await _plans.GetUserEffectivePlanAsync(h.OwnerId, ct);
            var label = HomeGamePrimary.DisplayNameFor(h.IsPrimary, ownerPlan, h.Name)
                ?? (h.OwnerId == viewerId
                    ? MePlayerService.OwnLabel
                    : string.IsNullOrWhiteSpace(h.OwnerName) ? h.Name : $"Torneios de {h.OwnerName.Trim()}");
            result.Add(new PlayerProfileHomeGame { Id = h.Id, Label = label, PublicRankings = h.Rankings });
        }
        return result;
    }

    /// <summary>Ids dos home games ativos (e não excluídos) em que a conta é dona, membro ativo ou tem inscrição vinculada.</summary>
    private async Task<HashSet<Guid>> HomeGameIdsAsync(Guid userId, CancellationToken ct)
    {
        var cancelled = nameof(TournamentStatus.Cancelled);
        var owned = _db.HomeGames.Where(h => h.IsActive && h.OwnerId == userId).Select(h => h.Id);
        var member = _db.HomeGameMembers
            .Where(m => m.UserId == userId && m.IsActive && m.HomeGame.IsActive && m.HomeGame.DeletedAt == null)
            .Select(m => m.HomeGameId);
        var played = _db.TournamentEntries.IgnoreQueryFilters()
            .Where(e => e.Person.UserId == userId
                && e.Tournament.DeletedAt == null
                && e.Tournament.Status != cancelled
                && e.Tournament.HomeGame.DeletedAt == null
                && e.Tournament.HomeGame.IsActive)
            .Select(e => e.Tournament.HomeGameId);
        var ids = await owned.Union(member).Union(played).ToListAsync(ct);
        return [.. ids];
    }

    /// <summary>
    /// Números do ano corrente: torneios finalizados com inscrição vinculada (reentrada conta uma vez — vale a última
    /// inscrição), vitórias (posição 1), vezes na premiação (status premiado ou prêmio > 0 — o valor NÃO sai) e a melhor
    /// posição.
    /// </summary>
    private async Task<PlayerSeasonStats> SeasonStatsAsync(Guid userId, CancellationToken ct)
    {
        var year = DateTimeOffset.UtcNow.ToOffset(BrasiliaOffset).Year;
        var start = new DateTimeOffset(year, 1, 1, 0, 0, 0, BrasiliaOffset);
        var end = start.AddYears(1);
        var finished = nameof(TournamentStatus.Finished);

        var rows = await _db.TournamentEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Person.UserId == userId
                && e.Tournament.DeletedAt == null
                && e.Tournament.Status == finished
                && e.Tournament.HomeGame.DeletedAt == null
                && e.Tournament.Date >= start
                && e.Tournament.Date < end)
            .Select(e => new { e.TournamentId, e.EntryNumber, e.RegisteredAt, e.Status, e.FinalPosition, e.PrizeAmount })
            .ToListAsync(ct);

        var latest = rows
            .GroupBy(r => r.TournamentId)
            .Select(g => g.OrderByDescending(r => r.EntryNumber).ThenByDescending(r => r.RegisteredAt).First())
            .ToList();

        var positions = latest.Where(r => r.FinalPosition is > 0).Select(r => r.FinalPosition!.Value).ToList();
        return new PlayerSeasonStats
        {
            Year = year,
            Tournaments = latest.Count,
            Wins = latest.Count(r => r.FinalPosition == 1),
            InTheMoney = latest.Count(r => ItmEntryStatuses.Contains(r.Status) || r.PrizeAmount > 0),
            BestPosition = positions.Count == 0 ? null : positions.Min(),
        };
    }

    // ------------------------------------------------------------------ escrita (Meu perfil)

    /// <summary>
    /// <c>PUT me/public-profile</c>: handle (normalizado e validado — formato, reservado e "já em uso" → 400), cidade
    /// (até 100), UF (lista oficial) e bio (até 200). Trocar o handle NÃO redireciona o antigo.
    /// </summary>
    public async Task<ProfileResponse> UpdatePublicProfileAsync(
        Guid userId, UpdatePublicProfileRequest request, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var handle = HandleGenerator.Normalize(request.Handle);
        var city = NullIfBlank(request.City);
        if (city is { Length: > CityMaxLength })
            throw new DomainException(CityTooLongMessage);
        string? state = null;
        if (!string.IsNullOrWhiteSpace(request.State))
        {
            state = request.State.Trim().ToUpperInvariant();
            if (!HomeGameAddress.IsState(state))
                throw new DomainException(HomeGameAddress.InvalidStateMessage);
        }
        var bio = NullIfBlank(request.Bio);
        if (bio is { Length: > BioMaxLength })
            throw new DomainException(BioTooLongMessage);

        var user = await _auth.FindActiveUserAsync(userId, ct)
            ?? throw new KeyNotFoundException(AuthService.UserNotFoundMessage);

        var changed = new List<string>();
        if (user.Handle != handle)
        {
            if (await _db.Users.AnyAsync(u => u.Handle == handle && u.Id != userId, ct))
                throw new DomainException(HandleGenerator.TakenMessage);
            user.Handle = handle;
            changed.Add(nameof(user.Handle));
        }
        if (user.City != city) { user.City = city; changed.Add(nameof(user.City)); }
        if (user.State != state) { user.State = state; changed.Add(nameof(user.State)); }
        if (user.Bio != bio) { user.Bio = bio; changed.Add(nameof(user.Bio)); }

        if (changed.Count > 0)
        {
            _auth.AddProfileAudit(user, PublicProfileUpdatedAction, null, JsonSerializer.Serialize(changed), ip, userAgent);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
            {
                // Corrida: outra conta pegou o mesmo handle entre a checagem e a gravação
                throw new DomainException(HandleGenerator.TakenMessage);
            }
        }
        return await _auth.ToProfileAsync(user, ct);
    }

    /// <summary><c>PUT me/privacy</c>: cada controle presente no pedido é validado e gravado; ausente = fica como está.</summary>
    public async Task<ProfileResponse> UpdatePrivacyAsync(
        Guid userId, UpdatePrivacyRequest request, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var visibility = request.ProfileVisibility is null ? null
            : ParseEnum<ProfileVisibility>(request.ProfileVisibility, InvalidVisibilityMessage);
        var friendRequests = request.FriendRequestsFrom is null ? null
            : ParseEnum<FriendRequestsFrom>(request.FriendRequestsFrom, InvalidFriendRequestsMessage);
        var messages = request.MessagesFrom is null ? null
            : ParseEnum<MessagesFrom>(request.MessagesFrom, InvalidMessagesMessage);

        var user = await _auth.FindActiveUserAsync(userId, ct)
            ?? throw new KeyNotFoundException(AuthService.UserNotFoundMessage);

        var changed = new List<string>();
        if (visibility is not null && user.ProfileVisibility != visibility)
        {
            user.ProfileVisibility = visibility;
            changed.Add(nameof(user.ProfileVisibility));
        }
        if (friendRequests is not null && user.FriendRequestsFrom != friendRequests)
        {
            user.FriendRequestsFrom = friendRequests;
            changed.Add(nameof(user.FriendRequestsFrom));
        }
        if (messages is not null && user.MessagesFrom != messages)
        {
            user.MessagesFrom = messages;
            changed.Add(nameof(user.MessagesFrom));
        }
        if (request.ShowInPlayedWith is { } show && user.ShowInPlayedWith != show)
        {
            user.ShowInPlayedWith = show;
            changed.Add(nameof(user.ShowInPlayedWith));
        }

        if (changed.Count > 0)
        {
            _auth.AddProfileAudit(user, PrivacyUpdatedAction, null, JsonSerializer.Serialize(changed), ip, userAgent);
            await _db.SaveChangesAsync(ct);
        }
        return await _auth.ToProfileAsync(user, ct);
    }

    /// <summary>Nome do enum como gravado (nameof), sem distinguir maiúsculas na entrada; inválido → 400.</summary>
    private static string ParseEnum<TEnum>(string value, string invalidMessage) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            return parsed.ToString();
        throw new DomainException(invalidMessage);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
