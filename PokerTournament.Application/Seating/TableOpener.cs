using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Seating;

/// <summary>
/// Ajudante único de "abrir mesa" (SPEC-20261023-1214, MA9): usado pela inscrição (planejador) e pelo "Criar mesa"
/// do painel. O número é único por torneio inclusive em mesa inativa (índice (tournament_id, table_number)), então
/// reaproveitar é REATIVAR a linha: quem ainda apontava para ela (mesa excluída sem desocupar) fica sem mesa e os
/// eliminados que apontavam para ela perdem a mesa/lugar de eliminação (desfazer não os devolve a um lugar de outra
/// mesa). As outras mesas ativas deixam de ser a mesa final. Não salva e não confere conflito com mesa ATIVA de
/// mesmo número (quem chama já sabe que o número está livre); exige a trava de lugares do torneio.
/// </summary>
public static class TableOpener
{
    public static async Task<TournamentTable> OpenAsync(
        IAppDbContext db, Guid tournamentId, int number, int maxSeats, string? name, CancellationToken ct)
    {
        var tableName = string.IsNullOrWhiteSpace(name) ? SeatingPlanner.TableName(number) : name;

        var existing = await db.TournamentTables
            .FirstOrDefaultAsync(t => t.TournamentId == tournamentId && t.TableNumber == number, ct);

        TournamentTable table;
        if (existing is not null)
        {
            existing.IsActive = true;
            existing.MaxSeats = maxSeats;
            existing.IsFinalTable = false;
            existing.TableName = tableName;
            existing.DealerPersonId = null;

            var stale = await db.TournamentEntries
                .Where(e => e.TournamentId == tournamentId
                         && (e.TableId == existing.Id || e.EliminationTableId == existing.Id))
                .ToListAsync(ct);
            foreach (var e in stale)
            {
                if (e.TableId == existing.Id)
                {
                    e.TableId = null;
                    e.SeatNumber = null;
                }
                if (e.EliminationTableId == existing.Id)
                {
                    e.EliminationTableId = null;
                    e.EliminationSeatNumber = null;
                }
            }

            table = existing;
        }
        else
        {
            table = new TournamentTable
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                TableNumber = number,
                TableName = tableName,
                MaxSeats = maxSeats,
                IsActive = true
            };
            db.TournamentTables.Add(table);
        }

        // Passou a haver mais de uma mesa ativa: nenhuma outra é mais a mesa final
        var otherActive = await db.TournamentTables
            .Where(t => t.TournamentId == tournamentId && t.IsActive && t.Id != table.Id)
            .ToListAsync(ct);
        foreach (var t in otherActive)
            t.IsFinalTable = false;

        return table;
    }
}
