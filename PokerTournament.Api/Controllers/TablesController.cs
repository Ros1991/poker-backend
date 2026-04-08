using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/[controller]")]
[Authorize]
public class TablesController : ControllerBase
{
    private readonly IAppDbContext _db;

    public TablesController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(Guid tournamentId, CancellationToken ct)
    {
        var tables = await _db.TournamentTables
            .Include(t => t.DealerPerson)
            .Include(t => t.Entries.Where(e => e.Status == nameof(EntryStatus.Active)))
                .ThenInclude(e => e.Person)
            .Where(t => t.TournamentId == tournamentId && t.IsActive)
            .OrderBy(t => t.TableNumber)
            .ToListAsync(ct);

        var result = tables.Select(t => new
        {
            t.Id,
            t.TableNumber,
            t.TableName,
            t.MaxSeats,
            t.IsFinalTable,
            t.DealerPersonId,
            DealerName = t.DealerPerson != null ? (t.DealerPerson.Nickname ?? t.DealerPerson.FullName) : null,
            PlayerCount = t.Entries.Count,
            Players = t.Entries.OrderBy(e => e.SeatNumber).Select(e => new
            {
                e.Id,
                e.SeatNumber,
                PersonId = e.PersonId,
                Name = e.Person.FullName,
                Photo = e.Person.PhotoUrl
            })
        });

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult> Create(
        Guid tournamentId, [FromBody] CreateTableRequest request, CancellationToken ct)
    {
        // Verificar se já existe mesa com mesmo número
        var existing = await _db.TournamentTables
            .AnyAsync(t => t.TournamentId == tournamentId && t.TableNumber == request.TableNumber, ct);

        if (existing)
            return Conflict(new { message = $"Já existe uma mesa com o número {request.TableNumber}." });

        var table = new TournamentTable
        {
            TournamentId = tournamentId,
            TableNumber = request.TableNumber,
            TableName = request.TableName ?? $"Mesa {request.TableNumber}",
            MaxSeats = request.MaxSeats,
            IsActive = true
        };

        _db.TournamentTables.Add(table);
        await _db.SaveChangesAsync(ct);

        return Ok(new { table.Id, table.TableNumber });
    }

    [HttpPost("draw")]
    
    public async Task<ActionResult> Draw(
        Guid tournamentId, [FromBody] DrawTablesRequest request, CancellationToken ct)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var activeEntries = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournamentId && (e.Status == nameof(EntryStatus.Active) || e.Status == nameof(EntryStatus.Registered)))
            .ToListAsync(ct);

        if (activeEntries.Count == 0)
            throw new DomainException("Não há jogadores ativos para sortear.");

        var maxSeats = request.MaxSeatsPerTable ?? tournament.SeatsPerTable;
        if (maxSeats < 2) maxSeats = 9;
        var tableCount = request.TableCount ?? (int)Math.Ceiling(activeEntries.Count / (double)maxSeats);
        if (tableCount < 1) tableCount = 1;

        // Limpar atribuições de assento dos jogadores antes de remover as mesas
        var allEntries = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournamentId)
            .ToListAsync(ct);
        foreach (var e in allEntries)
        {
            e.TableId = null;
            e.SeatNumber = null;
        }

        // Remover mesas existentes (hard delete)
        var existingTables = await _db.TournamentTables
            .Where(t => t.TournamentId == tournamentId)
            .ToListAsync(ct);
        _db.TournamentTables.RemoveRange(existingTables);
        await _db.SaveChangesAsync(ct);

        // Criar novas mesas
        var tables = new List<TournamentTable>();
        for (int i = 1; i <= tableCount; i++)
        {
            var table = new TournamentTable
            {
                TournamentId = tournamentId,
                TableNumber = i,
                TableName = $"Mesa {i}",
                MaxSeats = maxSeats,
                IsActive = true
            };
            _db.TournamentTables.Add(table);
            tables.Add(table);
        }

        // Sortear jogadores distribuindo uniformemente (round-robin)
        var shuffled = activeEntries.OrderBy(_ => Random.Shared.Next()).ToList();
        var seatCounters = new int[tableCount];
        for (int i = 0; i < shuffled.Count; i++)
        {
            var tableIndex = i % tableCount;
            seatCounters[tableIndex]++;
            shuffled[i].TableId = tables[tableIndex].Id;
            shuffled[i].SeatNumber = seatCounters[tableIndex];
        }

        await _db.SaveChangesAsync(ct);

        // Retornar lista de mesas criadas (ordem por tableNumber)
        var createdTables = tables
            .OrderBy(t => t.TableNumber)
            .Select(t => new
            {
                t.Id,
                t.TableNumber,
                t.TableName,
                t.MaxSeats,
                IsFinalTable = false,
                DealerPersonId = (Guid?)null,
                DealerName = (string?)null,
                PlayerCount = shuffled.Count(e => e.TableId == t.Id)
            })
            .ToList();

        return Ok(createdTables);
    }

    [HttpPost("balance")]
    
    public async Task<ActionResult> Balance(Guid tournamentId, CancellationToken ct)
    {
        var tables = await _db.TournamentTables
            .Include(t => t.Entries.Where(e => e.Status == nameof(EntryStatus.Active)))
            .Where(t => t.TournamentId == tournamentId && t.IsActive)
            .OrderBy(t => t.Entries.Count)
            .ToListAsync(ct);

        if (tables.Count < 2)
            return Ok(new { message = "Balanceamento não necessário." });

        // Balanceamento simples: mover do maior para o menor
        var maxTable = tables.OrderByDescending(t => t.Entries.Count).First();
        var minTable = tables.OrderBy(t => t.Entries.Count).First();

        while (maxTable.Entries.Count - minTable.Entries.Count > 1)
        {
            var entryToMove = maxTable.Entries.Last();
            entryToMove.TableId = minTable.Id;
            entryToMove.SeatNumber = minTable.Entries.Count + 1;

            maxTable = tables.OrderByDescending(t => t.Entries.Count).First();
            minTable = tables.OrderBy(t => t.Entries.Count).First();
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Mesas balanceadas com sucesso." });
    }

    [HttpPost("move")]
    public async Task<ActionResult> MovePlayer(
        Guid tournamentId, [FromBody] MovePlayerRequest request, CancellationToken ct)
    {
        var entry = await _db.TournamentEntries
            .FirstOrDefaultAsync(e => e.Id == request.EntryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        var table = await _db.TournamentTables
            .FirstOrDefaultAsync(t => t.Id == request.ToTableId && t.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Mesa não encontrada.");

        // Guardar a posição atual do jogador
        var fromTableId = entry.TableId;
        var fromSeat = entry.SeatNumber;

        // Verificar se já existe alguém no assento de destino
        var occupant = await _db.TournamentEntries
            .FirstOrDefaultAsync(e => e.TableId == request.ToTableId
                                   && e.SeatNumber == request.ToSeat
                                   && e.Id != request.EntryId
                                   && e.Status != nameof(EntryStatus.Eliminated), ct);

        if (occupant != null)
        {
            // Swap: trocar os dois jogadores de lugar
            occupant.TableId = fromTableId;
            occupant.SeatNumber = fromSeat;
        }

        entry.TableId = request.ToTableId;
        entry.SeatNumber = request.ToSeat;

        await _db.SaveChangesAsync(ct);
        return Ok(new {
            message = occupant != null ? "Jogadores trocados com sucesso." : "Jogador movido com sucesso."
        });
    }

    [HttpPut("{tableId:guid}/dealer")]
    public async Task<ActionResult> SetDealer(
        Guid tournamentId, Guid tableId, [FromBody] SetTableDealerRequest request, CancellationToken ct)
    {
        var table = await _db.TournamentTables
            .FirstOrDefaultAsync(t => t.Id == tableId && t.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Mesa não encontrada.");

        table.DealerPersonId = request.DealerPersonId;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Dealer atualizado." });
    }

    [HttpDelete("{tableId:guid}")]
    
    public async Task<ActionResult> Delete(Guid tournamentId, Guid tableId, CancellationToken ct)
    {
        var table = await _db.TournamentTables
            .FirstOrDefaultAsync(t => t.Id == tableId && t.TournamentId == tournamentId, ct);

        if (table is null)
            return NotFound(new { message = "Mesa não encontrada." });

        table.IsActive = false;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}

public class CreateTableRequest
{
    public int TableNumber { get; set; }
    public string? TableName { get; set; }
    public int MaxSeats { get; set; } = 9;
}
