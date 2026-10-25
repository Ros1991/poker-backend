namespace PokerTournament.Application.Common;

/// <summary>
/// Página e tamanho de página das listagens paginadas (GET admin/users, GET home-games/{id}/tournaments).
/// <para>
/// <c>(page - 1) * pageSize</c> em int estourava com page enorme (vira negativo) e page 0/negativa ou pageSize negativo
/// davam OFFSET/LIMIT negativos: o PostgreSQL respondia 2201X/2201W e a API devolvia 500. Aqui a página fica entre 1 e
/// <see cref="MaxPage"/>, o tamanho entre 1 e o máximo da listagem (≤ 0 = padrão) e o offset é calculado em long.
/// Página além do fim = lista vazia com 200 (quem chama confere <see cref="IsPastEnd"/> antes do Skip).
/// </para>
/// </summary>
public readonly record struct Paging(int Page, int PageSize, long Offset)
{
    /// <summary>Teto da página pedida (100 000 × 100 = 10 milhões de linhas: cabe no int do Skip com folga).</summary>
    public const int MaxPage = 100_000;

    public static Paging Normalize(int page, int pageSize, int defaultPageSize = 20, int maxPageSize = 100)
    {
        var safePage = Math.Clamp(page, 1, MaxPage);
        var safeSize = pageSize <= 0 ? defaultPageSize : Math.Min(pageSize, maxPageSize);
        return new Paging(safePage, safeSize, (safePage - 1L) * safeSize);
    }

    /// <summary>Offset no fim (ou além) do total, ou fora do int do Skip: a página vem vazia sem ir ao banco.</summary>
    public bool IsPastEnd(int totalCount) => Offset >= totalCount || Offset > int.MaxValue;

    /// <summary>Offset para o <c>Skip</c> — só depois de conferir <see cref="IsPastEnd"/>.</summary>
    public int Skip => (int)Offset;
}
