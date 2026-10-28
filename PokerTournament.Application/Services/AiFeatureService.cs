using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Ai;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>Quem está no aparelho: conta logada (id + Admin?) ou dealer do QR (pedido aprovado + torneio do token).</summary>
public sealed record AiDeviceCaller(Guid? UserId, bool IsAdmin, Guid? DealerRequestId, Guid? DealerTournamentId);

/// <summary>
/// Recursos de IA vistos pelo app (SPEC-20261027-1312): quais estão ligados agora (a caixa "Tirar o fundo" só aparece
/// com a IA e o recurso ligados) e o registro dos usos feitos no aparelho, que a tela de IA conta.
/// </summary>
public class AiFeatureService
{
    public const string NotOnDeviceMessage = "Este recurso de IA não roda no aparelho.";

    private readonly IAppDbContext _db;
    private readonly IAiSecretProtector _protector;
    private readonly AiRuntimeOptions _options;

    public AiFeatureService(IAppDbContext db, IAiSecretProtector protector, AiRuntimeOptions options)
    {
        _db = db;
        _protector = protector;
        _options = options;
    }

    /// <summary>
    /// Chaves dos recursos usáveis agora: IA ligada + recurso ligado; o recurso pago ainda precisa de chave legível e de
    /// limite do mês com folga.
    /// </summary>
    public async Task<AiAvailableFeaturesResponse> GetAvailableAsync(CancellationToken ct = default)
    {
        var response = new AiAvailableFeaturesResponse();
        var settings = await _db.AiSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == AiSettings.SingletonId, ct);
        // Sem a linha (banco recém-criado antes da subida): vale o padrão — IA ligada, recursos ligados, limite 0
        if (settings is { Enabled: false })
            return response;
        var rows = await _db.AiFeatures.AsNoTracking().ToListAsync(ct);
        bool? paidAllowed = null;
        foreach (var def in AiFeatureCatalog.For(_options.Development).OrderBy(f => f.SortOrder))
        {
            if (!(rows.FirstOrDefault(r => r.Key == def.Key)?.Enabled ?? true))
                continue;
            if (def.IsPaid)
            {
                paidAllowed ??= await PaidAllowedAsync(settings, ct);
                if (paidAllowed != true)
                    continue;
            }
            response.Features.Add(def.Key);
        }
        return response;
    }

    private async Task<bool> PaidAllowedAsync(AiSettings? settings, CancellationToken ct)
    {
        if (settings is null || string.IsNullOrEmpty(settings.ApiKeyEncrypted) || settings.MonthlyLimitUsd <= 0)
            return false;
        if (_protector.Unprotect(settings.ApiKeyEncrypted) is null)
            return false;
        var start = AiCost.MonthStart(DateTimeOffset.UtcNow);
        var spent = await _db.AiUsages.Where(u => u.CreatedAt >= start).SumAsync(u => (decimal?)u.CostUsd, ct) ?? 0m;
        return spent < settings.MonthlyLimitUsd;
    }

    /// <summary>
    /// Registra um uso feito no aparelho (sem custo). Recusa recurso desconhecido/que não roda no aparelho e recurso
    /// desligado. O home game vem do torneio do token (dealer) ou da pessoa/torneio informados, se a conta os acessa.
    /// </summary>
    public async Task RecordDeviceUsageAsync(RecordAiUsageRequest request, AiDeviceCaller caller, CancellationToken ct = default)
    {
        var def = AiFeatureCatalog.Find((request.Feature ?? string.Empty).Trim(), _options.Development)
            ?? throw new AiException(AiErrors.FeatureUnknown, AiErrors.FeatureUnknownMessage);
        if (!def.RunsOnDevice)
            throw new DomainException(NotOnDeviceMessage);

        var settings = await _db.AiSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == AiSettings.SingletonId, ct);
        if (settings is { Enabled: false })
            throw new AiException(AiErrors.AiDisabled, AiErrors.AiDisabledMessage);
        var enabled = await _db.AiFeatures.AsNoTracking().Where(f => f.Key == def.Key).Select(f => (bool?)f.Enabled).FirstOrDefaultAsync(ct);
        if (enabled == false)
            throw new AiException(AiErrors.FeatureDisabled, AiErrors.FeatureDisabledMessage);

        Guid? homeGameId = null;
        string? actorName = null;
        if (caller.DealerRequestId is { } requestId)
        {
            actorName = await _db.DealerRequests.AsNoTracking().Where(r => r.Id == requestId).Select(r => (string?)r.Name).FirstOrDefaultAsync(ct);
            actorName = actorName is null ? "Dealer" : $"{actorName} (dealer)";
            if (caller.DealerTournamentId is { } tid)
                homeGameId = await _db.Tournaments.AsNoTracking().Where(t => t.Id == tid).Select(t => (Guid?)t.HomeGameId).FirstOrDefaultAsync(ct);
        }
        else if (caller.UserId is { } userId)
        {
            actorName = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (string?)u.FullName).FirstOrDefaultAsync(ct);
            Guid? candidate = null;
            if (request.PersonId is { } personId)
                candidate = await _db.Persons.AsNoTracking().Where(p => p.Id == personId).Select(p => (Guid?)p.HomeGameId).FirstOrDefaultAsync(ct);
            if (candidate is null && request.TournamentId is { } tournamentId)
                candidate = await _db.Tournaments.AsNoTracking().Where(t => t.Id == tournamentId).Select(t => (Guid?)t.HomeGameId).FirstOrDefaultAsync(ct);
            if (candidate is { } hg && await HomeGameAccess.CanAccessAsync(_db, hg, userId, caller.IsAdmin, ct))
                homeGameId = hg;
        }

        _db.AiUsages.Add(new AiUsage
        {
            FeatureKey = def.Key,
            Model = null,
            OnDevice = true,
            UserId = caller.DealerRequestId is null ? caller.UserId : null,
            ActorName = actorName,
            HomeGameId = homeGameId,
            CostUsd = 0m,
            Outcome = nameof(AiUsageOutcome.Success),
        });
        await _db.SaveChangesAsync(ct);
    }
}
