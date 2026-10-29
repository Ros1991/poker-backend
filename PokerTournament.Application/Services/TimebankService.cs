using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Timebank;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Timebank virtual (SPEC-20261029-1722, D3/D4). O celular do dealer conta o tempo; aqui só se registra: o uso (−1, com os
/// segundos) e os ajustes do dealer ou do organizador (+1/−1), cada um uma <see cref="TournamentActivity"/> visível no
/// histórico do torneio (com quem fez) e desfazível por ele — como o rebuy e o add-on, que são transações. Tudo numa
/// gravação só (inscrição + atividade). O jogador nunca fica com menos de 0. Torneio sem timebank → 400 em tudo.
/// </summary>
public class TimebankService
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;

    public TimebankService(IAppDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public static readonly string UsedType = nameof(TournamentActivityType.TimebankUsed);
    public static readonly string AdjustedType = nameof(TournamentActivityType.TimebankAdjusted);

    public static bool IsTimebankActivity(string type) => type == UsedType || type == AdjustedType;

    /// <summary>
    /// "Usar timebank": tira 1 do jogador e grava TimebankUsed com os segundos do torneio. Com <c>ActionId</c> já gravado
    /// (o "Tentar de novo" do celular depois de uma falha de rede), devolve o registro que já existe sem descontar outra vez.
    /// </summary>
    public async Task<TimebankResultResponse> UseAsync(
        Guid tournamentId, Guid entryId, TimebankUseRequest? request, Guid? usedBy = null, CancellationToken ct = default)
    {
        var (tournament, entry) = await LoadAsync(tournamentId, entryId, ct);

        var actionId = request?.ActionId is { } requested && requested != Guid.Empty ? requested : (Guid?)null;
        if (actionId is { } id)
        {
            var existing = await _db.TournamentActivities.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id, ct);
            if (existing is not null)
            {
                if (existing.TournamentId != tournamentId || existing.EntryId != entryId || existing.Type != UsedType)
                    throw new DomainException("Pedido de timebank inválido.");
                return Result(entry, existing.Id, tournament, alreadyRegistered: true);
            }
        }

        EnsurePlayable(tournament, entry);
        if (entry.TimebanksRemaining <= 0)
            throw new DomainException(TimebankRules.NoneLeftMessage);

        var seconds = tournament.TimebankSeconds ?? TimebankRules.DefaultSeconds;
        entry.TimebanksRemaining--;
        var activity = new TournamentActivity
        {
            // O id do celular vira o id da atividade (o SaveChangesAsync só gera um quando vem vazio)
            Id = actionId ?? Guid.Empty,
            TournamentId = tournamentId,
            EntryId = entry.Id,
            Type = UsedType,
            Description = TimebankRules.UsedDescription(seconds, entry.TimebanksRemaining),
            Delta = -1,
            CreatedBy = usedBy
        };
        _db.TournamentActivities.Add(activity);

        await _db.SaveChangesAsync(ct);
        return Result(entry, activity.Id, tournament);
    }

    /// <summary>Dá (+1) ou tira (−1) um timebank: nunca abaixo de 0; pode passar do inicial (até 99). Grava TimebankAdjusted.</summary>
    public async Task<TimebankResultResponse> AdjustAsync(
        Guid tournamentId, Guid entryId, TimebankAdjustRequest? request, Guid? adjustedBy = null, CancellationToken ct = default)
    {
        var delta = request?.Delta ?? 0;
        if (delta != 1 && delta != -1)
            throw new DomainException(TimebankRules.InvalidDeltaMessage);

        var (tournament, entry) = await LoadAsync(tournamentId, entryId, ct);
        EnsurePlayable(tournament, entry);

        if (delta < 0 && entry.TimebanksRemaining <= 0)
            throw new DomainException(TimebankRules.NoneLeftMessage);
        if (delta > 0 && entry.TimebanksRemaining >= TimebankRules.MaxRemaining)
            throw new DomainException(TimebankRules.MaxRemainingMessage);

        entry.TimebanksRemaining += delta;
        var activity = new TournamentActivity
        {
            TournamentId = tournamentId,
            EntryId = entry.Id,
            Type = AdjustedType,
            Description = TimebankRules.AdjustedDescription(delta, entry.TimebanksRemaining),
            Delta = delta,
            CreatedBy = adjustedBy
        };
        _db.TournamentActivities.Add(activity);

        await _db.SaveChangesAsync(ct);
        return Result(entry, activity.Id, tournament);
    }

    /// <summary>
    /// Por que este item de timebank não se desfaz agora (null = pode): já desfeito, inscrição excluída, torneio que
    /// desligou o timebank, ou o desfazer deixaria o jogador abaixo de 0 / acima do máximo. Mesmas regras do <see cref="UndoAsync"/>.
    /// </summary>
    public static string? UndoBlockedReason(TournamentActivity activity, TournamentEntry? entry, bool timebankEnabled)
    {
        if (activity.UndoneAt is not null) return "Este item já foi desfeito.";
        if (entry is null) return "Inscrição não encontrada.";
        if (!timebankEnabled) return TimebankRules.DisabledMessage;
        var after = entry.TimebanksRemaining - (activity.Delta ?? 0);
        if (after < 0) return TimebankRules.UndoNoneLeftMessage;
        if (after > TimebankRules.MaxRemaining) return TimebankRules.MaxRemainingMessage;
        return null;
    }

    /// <summary>
    /// Desfaz um uso (devolve o timebank) ou um ajuste (faz o contrário) pelo histórico; marca a atividade como desfeita
    /// (UndoneAt/UndoneBy) — não apaga. Devolve a mensagem do toast.
    /// </summary>
    public async Task<string> UndoAsync(
        Guid tournamentId, Guid activityId, Guid? undoneBy = null, CancellationToken ct = default)
    {
        var activity = await _db.TournamentActivities
            .FirstOrDefaultAsync(a => a.Id == activityId && a.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Item não encontrado no histórico deste torneio.");

        if (!IsTimebankActivity(activity.Type))
            throw new DomainException("Este item não pode ser desfeito pelo terminal.");

        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var entry = activity.EntryId is { } entryId
            ? await _db.EntriesWithPerson().FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            : null;

        var blocked = UndoBlockedReason(activity, entry, TimebankRules.IsEnabled(tournament));
        if (blocked is not null)
            throw new DomainException(blocked);

        entry!.TimebanksRemaining -= activity.Delta ?? 0;
        activity.UndoneAt = DateTimeOffset.UtcNow;
        activity.UndoneBy = undoneBy;

        await _db.SaveChangesAsync(ct);
        return activity.Type == UsedType ? "Timebank devolvido." : "Ajuste de timebank desfeito.";
    }

    // ---------- apoio ----------

    private async Task<(Tournament Tournament, TournamentEntry Entry)> LoadAsync(Guid tournamentId, Guid entryId, CancellationToken ct)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (!TimebankRules.IsEnabled(tournament))
            throw new DomainException(TimebankRules.DisabledMessage);

        // Com a pessoa removida do cadastro também (EntryQueries): a resposta leva o nome dela
        var entry = await _db.EntriesWithPerson()
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        return (tournament, entry);
    }

    /// <summary>Mesmas fases do rebuy (rascunho, inscrições abertas, em andamento) e só quem ainda joga</summary>
    private static void EnsurePlayable(Tournament tournament, TournamentEntry entry)
    {
        if (tournament.Status != nameof(TournamentStatus.Draft)
            && tournament.Status != nameof(TournamentStatus.OpenForRegistration)
            && tournament.Status != nameof(TournamentStatus.InProgress))
            throw new DomainException(TimebankRules.PhaseMessage);

        if (entry.Status != nameof(EntryStatus.Active))
            throw new DomainException(TimebankRules.PlayerNotActiveMessage);
    }

    private TimebankResultResponse Result(TournamentEntry entry, Guid activityId, Tournament tournament, bool alreadyRegistered = false) =>
        new()
        {
            Entry = _mapper.Map<EntryResponse>(entry),
            ActivityId = activityId,
            Seconds = tournament.TimebankSeconds ?? TimebankRules.DefaultSeconds,
            AlreadyRegistered = alreadyRegistered
        };
}
