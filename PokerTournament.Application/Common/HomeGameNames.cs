using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Common;

/// <summary>
/// Nome de home game único POR DONO (revisão geral do programa, ondas 7–9). Donos diferentes podem repetir o nome; o
/// mesmo dono repetindo (criar ou renomear) recebe 400 "Você já tem um home game chamado X." — checado antes de gravar,
/// sob uma trava por dono (dois pedidos juntos do mesmo dono não passam os dois pela checagem), e também na corrida
/// que escapar dela (violação do índice <see cref="UniqueOwnerNameIndex"/> → a mesma mensagem). Excluídos não contam
/// (o índice e o filtro global ignoram deleted_at preenchido).
/// </summary>
public static class HomeGameNames
{
    /// <summary>Índice único parcial (owner_id, name) WHERE deleted_at IS NULL.</summary>
    public const string UniqueOwnerNameIndex = "ix_home_games_owner_name";

    // Espaço próprio da trava consultiva por dono (pg_advisory_xact_lock(int, int))
    private const int LockSpace = 1290;

    public static string DuplicateMessage(string name) => $"Você já tem um home game chamado {name}.";

    /// <summary>
    /// Trava o dono (dentro da transação aberta por quem chama) e recusa com 400 se ele já tem outro home game não
    /// excluído com o mesmo nome. <paramref name="exceptId"/> = o próprio home game, na edição.
    /// </summary>
    public static async Task EnsureAvailableAsync(
        IAppDbContext db, Guid ownerId, string name, Guid? exceptId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A checagem do nome do home game exige uma transação aberta.");

        var key = ownerId.ToString("N");
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockSpace}, hashtext({key}))", ct);

        var taken = await db.HomeGames.AnyAsync(
            h => h.OwnerId == ownerId && h.Name == name && (exceptId == null || h.Id != exceptId), ct);
        if (taken)
            throw new DomainException(DuplicateMessage(name));
    }

    /// <summary>Violação do índice único (owner_id, name) — PostgreSQL 23505 com o nome do índice na mensagem.</summary>
    public static bool IsDuplicateName(DbUpdateException ex) =>
        ex.InnerException is DbException { SqlState: "23505" } db
        && db.Message.Contains(UniqueOwnerNameIndex, StringComparison.Ordinal);
}
