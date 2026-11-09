using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Geo;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Enums;

namespace PokerTournament.Application.Services;

/// <summary>A página pública pronta + as coordenadas do clube (nunca na resposta): o controller calcula a distância do visitante.</summary>
public sealed record PublicTournamentData(PublicTournamentResponse Response, double? Latitude, double? Longitude);

/// <summary>
/// Página pública do torneio, <c>/t/{slug}</c> (SPEC-20261108-1639). Só torneio público, não cancelado, não excluído e de
/// home game vivo; finalizado responde com <c>IsOver</c> (a página diz "já aconteceu"). Nunca rua/número/CEP (D6), nem
/// e-mail, WhatsApp de jogador ou dinheiro de terceiros. O WhatsApp do organizador só com as duas portas (o torneio e o
/// opt-in da conta de quem responde — criador, senão dono — a mesma regra do <c>MePlayerService.OrganizerAsync</c>).
/// </summary>
public class PublicTournamentService
{
    private readonly IAppDbContext _db;
    private readonly PlanService _plans;
    private readonly JoinRequestService _joins;

    public PublicTournamentService(IAppDbContext db, PlanService plans, JoinRequestService joins)
    {
        _db = db;
        _plans = plans;
        _joins = joins;
    }

    /// <summary>Null = não disponível (404): slug desconhecido, torneio privado, cancelado, excluído ou de home game excluído/inativo.</summary>
    public async Task<PublicTournamentData?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var t = await _db.Tournaments.AsNoTracking()
            .Where(x => x.PublicSlug == slug && x.IsPublic && x.HomeGame.DeletedAt == null && x.HomeGame.IsActive)
            .Select(x => new
            {
                x.Id, x.Name, x.PublicDescription, x.Date, x.Status, x.HomeGameId, x.CreatedBy,
                x.BuyInAmount, x.RebuyAmount, x.AddonAmount, x.MaxRebuys, x.AddonAllowed, x.MaxAddons,
                x.StartingStack, x.RebuyStack, x.AddonStack, x.KnockoutType, x.BountyAmount, x.TimebankCount, x.TimebankSeconds,
                x.BlindStructureId, StructureName = x.BlindStructure != null ? x.BlindStructure.Name : null,
                x.MaxPlayers, x.NetPrizePool, x.ShowOrganizerWhatsapp,
                ClubName = x.HomeGame.Name, ClubIsPrimary = x.HomeGame.IsPrimary, ClubLogoUrl = x.HomeGame.LogoUrl,
                OwnerId = x.HomeGame.OwnerId, OwnerName = x.HomeGame.Owner.FullName,
                x.HomeGame.District, x.HomeGame.City, x.HomeGame.State, x.HomeGame.Latitude, x.HomeGame.Longitude,
            })
            .FirstOrDefaultAsync(ct);
        if (t is null || t.Status == nameof(TournamentStatus.Cancelled))
            return null;

        var ownerPlan = await _plans.GetUserEffectivePlanAsync(t.OwnerId, ct);
        var planDefinition = PlanCatalog.Get(ownerPlan);
        var (taken, left) = await _joins.SpotsAsync(t.Id, t.MaxPlayers, ct);
        var levels = await LoadLevelsAsync(t.Id, t.BlindStructureId, ct);
        var organizer = await OrganizerAsync(t.OwnerId, t.CreatedBy, t.ShowOrganizerWhatsapp, ct);

        var estimated = Math.Max(t.NetPrizePool, t.BuyInAmount * taken);
        var response = new PublicTournamentResponse
        {
            TournamentId = t.Id,
            Slug = slug,
            Name = t.Name,
            Description = t.PublicDescription,
            Date = t.Date,
            Status = t.Status,
            HasStarted = t.Status is nameof(TournamentStatus.InProgress) or nameof(TournamentStatus.BreakSettlement),
            IsOver = t.Status == nameof(TournamentStatus.Finished),
            ClubName = HomeGamePrimary.LabelFor(t.ClubIsPrimary, ownerPlan, t.ClubName, t.OwnerName),
            District = Clean(t.District),
            City = Clean(t.City),
            State = Clean(t.State),
            LocationSummary = JoinRequestService.LocationSummary(t.District, t.City, t.State),
            BuyInAmount = t.BuyInAmount,
            RebuyAmount = t.RebuyAmount is > 0 ? t.RebuyAmount : null,
            AddonAmount = t.AddonAllowed && t.AddonAmount is > 0 ? t.AddonAmount : null,
            MaxRebuys = t.MaxRebuys,
            AddonAllowed = t.AddonAllowed,
            MaxAddons = t.MaxAddons,
            StartingStack = t.StartingStack,
            RebuyStack = t.RebuyStack,
            AddonStack = t.AddonStack,
            KnockoutType = string.IsNullOrEmpty(t.KnockoutType) ? nameof(KnockoutType.None) : t.KnockoutType,
            BountyAmount = t.BountyAmount,
            TimebankCount = t.TimebankCount,
            TimebankSeconds = t.TimebankSeconds,
            StructureName = t.StructureName,
            LevelCount = levels.Count(l => !l.IsBreak),
            FirstLevel = levels.Where(l => !l.IsBreak).Select(l => new PublicBlindLevel
            {
                SmallBlind = l.SmallBlind, BigBlind = l.BigBlind, Ante = l.Ante, DurationMinutes = l.DurationMinutes,
            }).FirstOrDefault(),
            TypicalLevelMinutes = levels.Where(l => !l.IsBreak).GroupBy(l => l.DurationMinutes)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => (int?)g.Key).FirstOrDefault(),
            MaxPlayers = t.MaxPlayers,
            ConfirmedPlayers = taken,
            SpotsLeft = left,
            EstimatedPrizePool = estimated,
            FullHousePrizePool = t.MaxPlayers is { } max ? t.BuyInAmount * max : null,
            OrganizerName = organizer.Name,
            OrganizerPhotoUrl = organizer.PhotoUrl,
            OrganizerWhatsapp = organizer.Whatsapp,
            Plan = ownerPlan,
            ShowBadge = planDefinition.ShowBadge,
            ClubLogoUrl = planDefinition.Has(PlanFeature.ClubLogoOnTv) ? t.ClubLogoUrl : null,
        };
        return new PublicTournamentData(response, t.Latitude, t.Longitude);
    }

    /// <summary>Distância do visitante (km, 1 casa) quando ele mandou a localização e o clube tem coordenadas.</summary>
    public static double? Distance(PublicTournamentData data, double? lat, double? lng)
    {
        if (lat is not { } la || lng is not { } lo || data.Latitude is not { } hlat || data.Longitude is not { } hlng)
            return null;
        if (la is < -90 or > 90 || lo is < -180 or > 180)
            return null;
        return Math.Round(GeoDistance.Kilometers(la, lo, hlat, hlng), 1);
    }

    private sealed record Level(int SmallBlind, int BigBlind, int Ante, int DurationMinutes, bool IsBreak);

    private async Task<List<Level>> LoadLevelsAsync(Guid tournamentId, Guid? blindStructureId, CancellationToken ct)
    {
        var own = await _db.TournamentBlindLevels.AsNoTracking()
            .Where(l => l.TournamentId == tournamentId)
            .OrderBy(l => l.LevelNumber)
            .Select(l => new Level(l.SmallBlind, l.BigBlind, l.Ante, l.DurationMinutes, l.IsBreak))
            .ToListAsync(ct);
        if (own.Count > 0 || blindStructureId is null)
            return own;
        return await _db.BlindLevels.AsNoTracking()
            .Where(l => l.BlindStructureId == blindStructureId)
            .OrderBy(l => l.LevelNumber)
            .Select(l => new Level(l.SmallBlind, l.BigBlind, l.Ante, l.DurationMinutes, l.IsBreak))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Quem organiza: quem criou o torneio (se ainda existe e está ativo), senão o dono — nome público e foto. O WhatsApp
    /// só com a porta do torneio E o opt-in da conta de quem responde (criador com opt-in, senão dono com opt-in).
    /// </summary>
    private async Task<(string Name, string? PhotoUrl, string? Whatsapp)> OrganizerAsync(
        Guid ownerId, Guid? createdBy, bool showWhatsapp, CancellationToken ct)
    {
        var ids = new List<Guid> { ownerId };
        if (createdBy is { } creator && creator != ownerId)
            ids.Insert(0, creator);
        var users = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.FullName, u.Nickname, u.PhotoUrl, u.Whatsapp, u.ShowWhatsappToPlayers })
            .ToListAsync(ct);
        var shown = ids.Select(id => users.FirstOrDefault(u => u.Id == id)).FirstOrDefault(u => u is not null);
        var name = shown is null ? "Organizador" : string.IsNullOrWhiteSpace(shown.Nickname) ? shown.FullName : shown.Nickname;
        string? whatsapp = null;
        if (showWhatsapp)
        {
            foreach (var id in ids)
            {
                var u = users.FirstOrDefault(x => x.Id == id);
                if (u is null || !u.ShowWhatsappToPlayers)
                    continue;
                var digits = MePlayerService.WhatsappDigits(u.Whatsapp);
                if (digits is not null)
                {
                    whatsapp = digits;
                    break;
                }
            }
        }
        return (name, shown?.PhotoUrl, whatsapp);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
