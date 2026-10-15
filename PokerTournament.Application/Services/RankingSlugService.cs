using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Application.Services;

/// <summary>Endereço público (/r/{slug}) dos rankings.</summary>
public class RankingSlugService
{
    private readonly IAppDbContext _db;

    public RankingSlugService(IAppDbContext db)
    {
        _db = db;
    }

    /// <summary>Próximo slug livre para um ranking novo com este nome.</summary>
    public async Task<string> NextAvailableAsync(string name, CancellationToken ct = default)
    {
        var taken = await GetTakenAsync(ct);
        return SlugGenerator.MakeUnique(SlugGenerator.FromName(name), taken);
    }

    /// <summary>
    /// Gera slug para os rankings vivos que ainda não têm (idempotente), em ordem
    /// de criação. Excluídos ficam sem slug, para que um excluído homônimo não
    /// pegue o endereço "limpo" no lugar do ranking vivo.
    /// </summary>
    public async Task<int> AssignMissingAsync(CancellationToken ct = default)
    {
        // O filtro global de soft-delete já deixa os excluídos de fora
        var missing = await _db.Rankings
            .Where(r => r.Slug == null)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        if (missing.Count == 0)
            return 0;

        var taken = await GetTakenAsync(ct);
        foreach (var ranking in missing)
        {
            ranking.Slug = SlugGenerator.MakeUnique(SlugGenerator.FromName(ranking.Name), taken);
            taken.Add(ranking.Slug);
        }

        await _db.SaveChangesAsync(ct);
        return missing.Count;
    }

    // Considera também os excluídos que têm slug: um link antigo nunca passa a
    // apontar para outro ranking.
    private async Task<HashSet<string>> GetTakenAsync(CancellationToken ct) =>
        (await _db.Rankings
            .IgnoreQueryFilters()
            .Where(r => r.Slug != null)
            .Select(r => r.Slug!)
            .ToListAsync(ct))
        .ToHashSet();
}
