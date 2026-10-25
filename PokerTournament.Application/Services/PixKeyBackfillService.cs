using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Pix;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Services;

/// <summary>
/// Normalização das chaves PIX já gravadas (SPEC-20261023-1218, PX11): roda no boot (Program.cs), idempotente, com o
/// MESMO <see cref="PixKeyNormalizer"/> da gravação — em vez de uma migration SQL, que seria uma terceira cópia da regra.
///
/// Percorre torneios (PIX do dia) e home games, inclusive os excluídos (soft delete), custos e custos padrão das
/// estruturas. Para cada chave: normaliza; se mudou (texto ou, nos custos, o tipo gravado), atualiza e grava o original
/// em <c>audit_logs</c> (<c>Action = "PixKeyNormalized"</c>, <c>UserName = "boot"</c>) — dá para reverter com um UPDATE a
/// partir do <c>old_values</c>. Igual → nada. Fora de formato → fica como está (nada se perde) e vira um Warning no log
/// com o tipo e o id da linha, SEM a chave; o painel avisa o organizador. Caso ambíguo (11 dígitos que são CPF válido
/// e celular): continua CPF, que é como os bancos já o leem hoje. Um SaveChanges no fim.
/// </summary>
public class PixKeyBackfillService
{
    public const string AuditAction = "PixKeyNormalized";

    private readonly IAppDbContext _db;
    private readonly ILogger<PixKeyBackfillService> _logger;

    public PixKeyBackfillService(IAppDbContext db, ILogger<PixKeyBackfillService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Normaliza o que falta; devolve quantas chaves mudaram e quantas ficaram fora de formato.</summary>
    public async Task<(int Changed, int Invalid)> NormalizeExistingAsync(CancellationToken ct = default)
    {
        var changed = 0;
        var invalid = 0;
        var now = DateTimeOffset.UtcNow;

        var tournaments = await _db.Tournaments
            .IgnoreQueryFilters()
            .Where(t => t.ResponsiblePixKey != null)
            .ToListAsync(ct);
        foreach (var t in tournaments)
        {
            var result = PixKeyNormalizer.Normalize(t.ResponsiblePixKey);
            if (!result.Ok || result.Key is null)
            {
                invalid++;
                Warn("Tournament", t.Id);
                continue;
            }
            if (result.Key == t.ResponsiblePixKey)
                continue;
            AddAudit("Tournament", t.Id, t.Id, new { PixKey = t.ResponsiblePixKey },
                new { PixKey = result.Key, PixKeyType = result.Type?.ToString() }, now);
            t.ResponsiblePixKey = result.Key;
            changed++;
        }

        var homeGames = await _db.HomeGames
            .IgnoreQueryFilters()
            .Where(h => h.PixKey != null)
            .ToListAsync(ct);
        foreach (var h in homeGames)
        {
            var result = PixKeyNormalizer.Normalize(h.PixKey);
            if (!result.Ok || result.Key is null)
            {
                invalid++;
                Warn("HomeGame", h.Id);
                continue;
            }
            if (result.Key == h.PixKey)
                continue;
            AddAudit("HomeGame", h.Id, null, new { PixKey = h.PixKey },
                new { PixKey = result.Key, PixKeyType = result.Type?.ToString() }, now);
            h.PixKey = result.Key;
            changed++;
        }

        var costs = await _db.CostExtras
            .IgnoreQueryFilters()
            .Where(c => c.PixKey != null)
            .ToListAsync(ct);
        foreach (var c in costs)
        {
            var (ok, key, type) = NormalizeWithStoredType(c.PixKey, c.PixKeyType);
            if (!ok)
            {
                invalid++;
                Warn("CostExtra", c.Id);
                continue;
            }
            if (key == c.PixKey && type == c.PixKeyType)
                continue;
            AddAudit("CostExtra", c.Id, c.TournamentId, new { PixKey = c.PixKey, PixKeyType = c.PixKeyType },
                new { PixKey = key, PixKeyType = type }, now);
            c.PixKey = key;
            c.PixKeyType = type;
            changed++;
        }

        var defaultCosts = await _db.BlindStructureDefaultCosts
            .Where(c => c.PixKey != null)
            .ToListAsync(ct);
        foreach (var c in defaultCosts)
        {
            var (ok, key, type) = NormalizeWithStoredType(c.PixKey, c.PixKeyType);
            if (!ok)
            {
                invalid++;
                Warn("BlindStructureDefaultCost", c.Id);
                continue;
            }
            if (key == c.PixKey && type == c.PixKeyType)
                continue;
            AddAudit("BlindStructureDefaultCost", c.Id, null, new { PixKey = c.PixKey, PixKeyType = c.PixKeyType },
                new { PixKey = key, PixKeyType = type }, now);
            c.PixKey = key;
            c.PixKeyType = type;
            changed++;
        }

        if (changed > 0)
            await _db.SaveChangesAsync(ct);
        return (changed, invalid);
    }

    /// <summary>
    /// Custos guardam o tipo: um tipo gravado que é um PixKeyType entra como tipo escolhido (e precisa valer para a
    /// chave); senão, detecção automática. O tipo resultante é sempre o da chave normalizada.
    /// </summary>
    private static (bool Ok, string? Key, string? Type) NormalizeWithStoredType(string? key, string? storedType)
    {
        var chosen = PixKeyNormalizer.TryParseType(storedType, out var parsed) && parsed is not null
            ? parsed.ToString()
            : null;
        var result = PixKeyNormalizer.Normalize(key, chosen);
        if (!result.Ok && chosen is not null)
            result = PixKeyNormalizer.Normalize(key);
        if (!result.Ok || result.Key is null)
            return (false, null, null);
        return (true, result.Key, result.Type?.ToString());
    }

    private void Warn(string entity, Guid id) =>
        // Sem o valor da chave no log (é dado de recebimento de alguém)
        _logger.LogWarning("Chave PIX em formato não aceito: {Entity:l} {Id}", entity, id);

    private void AddAudit(string entity, Guid id, Guid? tournamentId, object oldValues, object newValues, DateTimeOffset now) =>
        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = entity,
            EntityId = id,
            Action = AuditAction,
            OldValues = JsonSerializer.Serialize(oldValues),
            NewValues = JsonSerializer.Serialize(newValues),
            UserName = "boot",
            TournamentId = tournamentId,
            CreatedAt = now,
        });
}
