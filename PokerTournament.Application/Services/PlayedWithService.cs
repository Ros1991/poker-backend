using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Application.Services;

/// <summary>
/// "Jogou com você" (SPEC-20261108-1636): as contas vinculadas a inscrições dos MESMOS torneios da conta logada
/// (qualquer status de inscrição; torneios não cancelados nem excluídos, em home games não excluídos), exceto ela
/// mesma, só quando os DOIS lados estão com <c>ShowInPlayedWith</c> ligado — com a própria opção desligada a lista
/// vem vazia (o Início esconde o bloco). Bloqueados (<see cref="IFriendshipLookup"/>) saem. Por conta: quantos
/// torneios em comum e a data do mais recente; ordem por vezes e depois por data. Só foto, apelido/primeiro nome,
/// handle e cidade — nada de nome completo, e-mail ou dinheiro. Sugestão de amizade a partir da SPEC-20261108-1637.
/// </summary>
public class PlayedWithService
{
    public const int DefaultLimit = 8;
    public const int MaxLimit = 50;

    private readonly IAppDbContext _db;
    private readonly IFriendshipLookup _friends;

    public PlayedWithService(IAppDbContext db, IFriendshipLookup friends)
    {
        _db = db;
        _friends = friends;
    }

    public async Task<List<PlayedWithResponse>> ListAsync(Guid userId, int limit, CancellationToken ct = default)
    {
        var take = limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

        var me = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.ShowInPlayedWith })
            .FirstOrDefaultAsync(ct);
        if (me is null || !me.ShowInPlayedWith)
            return [];

        var cancelled = nameof(TournamentStatus.Cancelled);
        // Os torneios da conta (inscrição vinculada), fora cancelados/excluídos — IgnoreQueryFilters solta o jogador
        // removido do cadastro (ele continua no torneio), então os excluídos saem à mão
        var myTournaments = _db.TournamentEntries.IgnoreQueryFilters()
            .Where(e => e.Person.UserId == userId
                && e.Tournament.DeletedAt == null
                && e.Tournament.Status != cancelled
                && e.Tournament.HomeGame.DeletedAt == null)
            .Select(e => e.TournamentId);

        // As outras contas nesses torneios, já filtradas pelo lado delas (ativa, confirmada, aparecer ligado)
        var pairs = await _db.TournamentEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => myTournaments.Contains(e.TournamentId)
                && e.Person.UserId != null
                && e.Person.UserId != userId
                && e.Person.User!.IsActive
                && e.Person.User.EmailConfirmedAt != null
                && e.Person.User.ShowInPlayedWith)
            .Select(e => new { UserId = e.Person.UserId!.Value, e.TournamentId, e.Tournament.Date })
            .Distinct()
            .ToListAsync(ct);
        if (pairs.Count == 0)
            return [];

        var ranked = pairs
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Times = g.Count(), Last = g.Max(p => p.Date) })
            .OrderByDescending(x => x.Times)
            .ThenByDescending(x => x.Last)
            .ThenBy(x => x.UserId)
            .ToList();

        var ids = ranked.Select(r => r.UserId).ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Handle, u.Nickname, u.FullName, u.PhotoUrl, u.City, u.State })
            .ToDictionaryAsync(u => u.Id, ct);

        var result = new List<PlayedWithResponse>(take);
        foreach (var r in ranked)
        {
            if (result.Count >= take)
                break;
            if (!users.TryGetValue(r.UserId, out var u))
                continue;
            if (await _friends.IsBlockedAsync(userId, r.UserId, ct))
                continue;
            result.Add(new PlayedWithResponse
            {
                Id = u.Id,
                Handle = u.Handle,
                DisplayName = PlayerProfileService.DisplayNameOf(u.Nickname, u.FullName),
                PhotoUrl = u.PhotoUrl,
                City = u.City,
                State = u.State,
                TimesPlayed = r.Times,
                LastPlayedAt = r.Last,
            });
        }
        return result;
    }
}
