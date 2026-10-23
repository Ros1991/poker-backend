using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PokerTournament.Api.Authorization;
using PokerTournament.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Services;
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

    [AllowTokenKind(TokenKinds.Dealer)]
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

    // Faixa de lugares por mesa aceita no sorteio e na criação (o terminal desenha 1..MaxSeats)
    private const int MinSeatsPerTable = 2;
    private const int MaxSeatsPerTable = 12;

    [HttpPost]
    public async Task<ActionResult> Create(
        Guid tournamentId, [FromBody] CreateTableRequest request, CancellationToken ct)
    {
        if (request.TableNumber < 1)
            throw new DomainException("O número da mesa deve ser maior que zero.");
        if (request.MaxSeats < MinSeatsPerTable || request.MaxSeats > MaxSeatsPerTable)
            throw new DomainException($"A mesa deve ter de {MinSeatsPerTable} a {MaxSeatsPerTable} lugares.");

        await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
        await EntryService.LockSeatingAsync(_db, tournamentId, ct);

        // O número é único por torneio (inclusive mesas quebradas/excluídas)
        var existing = await _db.TournamentTables
            .FirstOrDefaultAsync(t => t.TournamentId == tournamentId && t.TableNumber == request.TableNumber, ct);

        if (existing is { IsActive: true })
            return Conflict(new { message = $"Já existe uma mesa com o número {request.TableNumber}." });

        TournamentTable table;
        if (existing is not null)
        {
            // Mesa inativa (quebrada ou excluída) — o painel não a mostra: reativa como mesa nova
            // em vez de recusar o número
            existing.IsActive = true;
            existing.MaxSeats = request.MaxSeats;
            existing.IsFinalTable = false;
            existing.TableName = request.TableName ?? $"Mesa {request.TableNumber}";
            existing.DealerPersonId = null;

            // Quem ainda apontava para ela (mesa excluída sem desocupar) já aparecia sem mesa:
            // continua sem mesa, em vez de reaparecer num lugar antigo
            var stale = await _db.TournamentEntries
                .Where(e => e.TournamentId == tournamentId && e.TableId == existing.Id)
                .ToListAsync(ct);
            foreach (var e in stale)
            {
                e.TableId = null;
                e.SeatNumber = null;
            }

            table = existing;
        }
        else
        {
            table = new TournamentTable
            {
                TournamentId = tournamentId,
                TableNumber = request.TableNumber,
                TableName = request.TableName ?? $"Mesa {request.TableNumber}",
                MaxSeats = request.MaxSeats,
                IsActive = true
            };
            _db.TournamentTables.Add(table);
        }

        // Passou a haver mais de uma mesa ativa: nenhuma é mais a mesa final
        var otherActive = await _db.TournamentTables
            .Where(t => t.TournamentId == tournamentId && t.IsActive && t.Id != table.Id)
            .ToListAsync(ct);
        foreach (var t in otherActive)
            t.IsFinalTable = false;

        await _db.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);

        return Ok(new { table.Id, table.TableNumber });
    }

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("draw")]
    public async Task<ActionResult> Draw(
        Guid tournamentId, [FromBody] DrawTablesRequest request, CancellationToken ct)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
        await EntryService.LockSeatingAsync(_db, tournamentId, ct);

        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var activeEntries = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournamentId && (e.Status == nameof(EntryStatus.Active) || e.Status == nameof(EntryStatus.Registered)))
            .ToListAsync(ct);

        if (activeEntries.Count == 0)
            throw new DomainException("Não há jogadores ativos para sortear.");

        // Limites do servidor (o dealer por QR também sorteia): lugares por mesa em 2..12 e
        // no máximo uma mesa por jogador ativo
        int maxSeats;
        if (request.MaxSeatsPerTable.HasValue)
        {
            maxSeats = request.MaxSeatsPerTable.Value;
            if (maxSeats < MinSeatsPerTable || maxSeats > MaxSeatsPerTable)
                throw new DomainException($"Lugares por mesa: de {MinSeatsPerTable} a {MaxSeatsPerTable}.");
        }
        else
        {
            // Padrão do torneio, trazido para a faixa aceita
            maxSeats = tournament.SeatsPerTable < MinSeatsPerTable ? 9 : Math.Min(tournament.SeatsPerTable, MaxSeatsPerTable);
        }

        int tableCount;
        if (request.TableCount.HasValue)
        {
            tableCount = request.TableCount.Value;
            if (tableCount < 1 || tableCount > activeEntries.Count)
                throw new DomainException(activeEntries.Count == 1
                    ? "Com 1 jogador ativo, só dá para sortear 1 mesa."
                    : $"Número de mesas: de 1 a {activeEntries.Count} (não pode haver mais mesas que jogadores ativos).");
        }
        else
        {
            tableCount = (int)Math.Ceiling(activeEntries.Count / (double)maxSeats);
        }

        if (tableCount * maxSeats < activeEntries.Count)
            throw new DomainException(
                $"{Plural(tableCount, "mesa", "mesas")} de {maxSeats} lugares não comportam {Plural(activeEntries.Count, "jogador", "jogadores")}.");

        // Limpar atribuições de assento dos jogadores antes de remover as mesas. A mesa/lugar
        // de eliminação também é limpa: aponta para mesa que vai sumir (desfazer a eliminação
        // devolveria o jogador a uma mesa inexistente).
        var allEntries = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournamentId)
            .ToListAsync(ct);
        foreach (var e in allEntries)
        {
            e.TableId = null;
            e.SeatNumber = null;
            e.EliminationTableId = null;
            e.EliminationSeatNumber = null;
        }

        // Remover mesas existentes (hard delete). eliminations.table_id é SET NULL no banco;
        // anulado aqui também para não depender só da FK.
        var existingTables = await _db.TournamentTables
            .Where(t => t.TournamentId == tournamentId)
            .ToListAsync(ct);
        var hadTables = existingTables.Count > 0;
        if (hadTables)
        {
            var existingIds = existingTables.Select(t => t.Id).ToList();
            var eliminationsAtTables = await _db.Eliminations
                .Where(e => e.TournamentId == tournamentId && e.TableId != null && existingIds.Contains(e.TableId.Value))
                .ToListAsync(ct);
            foreach (var el in eliminationsAtTables)
                el.TableId = null;
        }
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

        _db.TournamentActivities.Add(new TournamentActivity
        {
            TournamentId = tournamentId,
            Type = nameof(TournamentActivityType.TablesDrawn),
            Description = $"{(hadTables ? "Mesas sorteadas de novo" : "Mesas sorteadas")}: "
                        + $"{Plural(tableCount, "mesa", "mesas")}, {Plural(shuffled.Count, "jogador", "jogadores")}",
            CreatedBy = GetUserId()
        });

        await _db.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);

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

    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("move")]
    public async Task<ActionResult> MovePlayer(
        Guid tournamentId, [FromBody] MovePlayerRequest request, CancellationToken ct)
    {
        // Trava de lugares antes de qualquer leitura: dois moves para o mesmo lugar livre
        // não podem ler "livre" ao mesmo tempo
        await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
        await EntryService.LockSeatingAsync(_db, tournamentId, ct);

        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == request.EntryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        if (!IsPlaying(entry.Status))
            throw new DomainException("Jogador não está ativo no torneio.");

        var table = await _db.TournamentTables
            .FirstOrDefaultAsync(t => t.Id == request.ToTableId && t.TournamentId == tournamentId && t.IsActive, ct)
            ?? throw new DomainException("Mesa não encontrada.");

        if (request.ToSeat < 1 || request.ToSeat > table.MaxSeats)
            throw new DomainException($"Lugar inválido: a {EntryService.TableDisplayName(table)} tem lugares de 1 a {table.MaxSeats}.");

        if (entry.TableId == table.Id && entry.SeatNumber == request.ToSeat)
            throw new DomainException("O jogador já está nesse lugar.");

        // Guardar a posição atual do jogador
        var fromTableId = entry.TableId;
        var fromSeat = entry.SeatNumber;
        var fromTable = fromTableId.HasValue
            ? await _db.TournamentTables.FirstOrDefaultAsync(t => t.Id == fromTableId, ct)
            : null;

        // Ocupante = jogador ainda no torneio sentado no lugar de destino
        var occupant = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.TournamentId == tournamentId
                                   && e.TableId == request.ToTableId
                                   && e.SeatNumber == request.ToSeat
                                   && e.Id != request.EntryId
                                   && (e.Status == nameof(EntryStatus.Active) || e.Status == nameof(EntryStatus.Registered)), ct);

        var isBalance = string.Equals(request.Reason, "balance", StringComparison.OrdinalIgnoreCase);

        // Balanceamento sorteou um lugar LIVRE; se alguém sentou nele nesse meio-tempo (desfazer
        // eliminação, outro aparelho), não troca ninguém: o terminal sorteia de novo
        if (occupant != null && isBalance)
            throw new DomainException("O lugar de destino já está ocupado. Sorteie de novo.");

        // Troca só faz sentido entre dois jogadores sentados (senão o ocupante ficaria sem mesa)
        if (occupant != null && (fromTable is null || fromSeat is null))
            throw new DomainException("Esse lugar está ocupado. Escolha um lugar livre.");

        // Entrar numa mesa sem trocar com ninguém: a lotação não pode passar do MaxSeats
        // (dados antigos podem ter jogador em lugar acima do MaxSeats)
        if (occupant is null && entry.TableId != table.Id)
        {
            var seatedAtTarget = await _db.TournamentEntries
                .CountAsync(e => e.TournamentId == tournamentId
                              && e.TableId == table.Id
                              && (e.Status == nameof(EntryStatus.Active) || e.Status == nameof(EntryStatus.Registered)), ct);
            if (seatedAtTarget >= table.MaxSeats)
                throw new DomainException($"A {EntryService.TableDisplayName(table)} está cheia.");
        }

        if (occupant != null)
        {
            // Swap: trocar os dois jogadores de lugar
            occupant.TableId = fromTableId;
            occupant.SeatNumber = fromSeat;
        }

        entry.TableId = request.ToTableId;
        entry.SeatNumber = request.ToSeat;

        var prefix = isBalance ? "Balanceamento — " : string.Empty;
        var name = EntryService.PlayerDisplayName(entry.Person);
        var fromText = SeatText(fromTable, fromSeat);
        var toText = SeatText(table, request.ToSeat);
        var userId = GetUserId();

        if (occupant != null)
        {
            var otherName = EntryService.PlayerDisplayName(occupant.Person);
            _db.TournamentActivities.Add(new TournamentActivity
            {
                TournamentId = tournamentId,
                EntryId = entry.Id,
                Type = nameof(TournamentActivityType.PlayersSwapped),
                Description = $"{prefix}{name} trocou de lugar com {otherName}: {fromText} → {toText}",
                CreatedBy = userId
            });
            // Mesma troca vista pelo outro jogador (aparece no histórico dele)
            _db.TournamentActivities.Add(new TournamentActivity
            {
                TournamentId = tournamentId,
                EntryId = occupant.Id,
                Type = nameof(TournamentActivityType.PlayersSwapped),
                Description = $"{prefix}{otherName} trocou de lugar com {name}: {toText} → {fromText}",
                CreatedBy = userId,
                PlayerOnly = true
            });
        }
        else
        {
            _db.TournamentActivities.Add(new TournamentActivity
            {
                TournamentId = tournamentId,
                EntryId = entry.Id,
                Type = nameof(TournamentActivityType.PlayerMoved),
                Description = $"{prefix}{name}: {fromText} → {toText}",
                CreatedBy = userId
            });
        }

        await _db.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);
        return Ok(new {
            message = occupant != null ? "Jogadores trocados com sucesso." : "Jogador movido com sucesso."
        });
    }

    /// <summary>
    /// Quebra uma mesa: sorteia os jogadores ativos dela nos lugares livres das outras mesas
    /// ativas, sempre na mesa com menos gente no momento (empate → aleatório), em lugar livre
    /// aleatório. A mesa quebrada fica inativa (não é apagada). Se sobrar 1 mesa, vira a final.
    /// </summary>
    [AllowTokenKind(TokenKinds.Dealer)]
    [HttpPost("{tableId:guid}/break")]
    public async Task<ActionResult<BreakTableResponse>> BreakTable(
        Guid tournamentId, Guid tableId, CancellationToken ct)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
        await EntryService.LockSeatingAsync(_db, tournamentId, ct);

        var activeTables = await _db.TournamentTables
            .Where(t => t.TournamentId == tournamentId && t.IsActive)
            .OrderBy(t => t.TableNumber)
            .ToListAsync(ct);

        var broken = activeTables.FirstOrDefault(t => t.Id == tableId)
            ?? throw new DomainException("Mesa não encontrada.");

        var others = activeTables.Where(t => t.Id != tableId).ToList();
        if (others.Count == 0)
            throw new DomainException("Não há outra mesa ativa para receber os jogadores.");

        var activeTableIds = activeTables.Select(t => t.Id).ToList();
        var seated = await _db.TournamentEntries
            .Include(e => e.Person)
            .Where(e => e.TournamentId == tournamentId
                     && (e.Status == nameof(EntryStatus.Active) || e.Status == nameof(EntryStatus.Registered))
                     && e.TableId != null
                     && activeTableIds.Contains(e.TableId.Value))
            .ToListAsync(ct);

        var movers = seated.Where(e => e.TableId == broken.Id).ToList();

        // Lugares livres por mesa de destino (1..MaxSeats sem jogador ativo)
        var freeSeats = others.ToDictionary(
            t => t.Id,
            t =>
            {
                var taken = seated.Where(e => e.TableId == t.Id && e.SeatNumber != null)
                    .Select(e => e.SeatNumber!.Value).ToHashSet();
                return Enumerable.Range(1, t.MaxSeats).Where(s => !taken.Contains(s)).ToList();
            });
        var counts = others.ToDictionary(t => t.Id, t => seated.Count(e => e.TableId == t.Id));

        // Vaga real por mesa = menor entre os números de lugar livres e a lotação que falta
        // (jogador em lugar acima do MaxSeats ocupa vaga sem ocupar número de 1..MaxSeats)
        int Capacity(TournamentTable t) => Math.Min(freeSeats[t.Id].Count, Math.Max(0, t.MaxSeats - counts[t.Id]));

        if (others.Sum(Capacity) < movers.Count)
            throw new DomainException("Não há lugares livres suficientes nas outras mesas.");

        var brokenName = EntryService.TableDisplayName(broken);
        var userId = GetUserId();
        var moves = new List<BreakTableMove>();

        foreach (var mover in movers.OrderBy(_ => Random.Shared.Next()))
        {
            var open = others.Where(t => Capacity(t) > 0).ToList();
            var minCount = open.Min(t => counts[t.Id]);
            var candidates = open.Where(t => counts[t.Id] == minCount).ToList();
            var target = candidates[Random.Shared.Next(candidates.Count)];

            var free = freeSeats[target.Id];
            var seatIdx = Random.Shared.Next(free.Count);
            var seat = free[seatIdx];
            free.RemoveAt(seatIdx);
            counts[target.Id]++;

            mover.TableId = target.Id;
            mover.SeatNumber = seat;

            var playerName = EntryService.PlayerDisplayName(mover.Person);
            var targetName = EntryService.TableDisplayName(target);
            moves.Add(new BreakTableMove
            {
                EntryId = mover.Id,
                PlayerName = playerName,
                ToTableId = target.Id,
                ToTableName = targetName,
                ToSeat = seat
            });

            _db.TournamentActivities.Add(new TournamentActivity
            {
                TournamentId = tournamentId,
                EntryId = mover.Id,
                Type = nameof(TournamentActivityType.PlayerMoved),
                Description = $"{brokenName} quebrada — {playerName} foi para {targetName}, lugar {seat}",
                CreatedBy = userId,
                PlayerOnly = true
            });
        }

        broken.IsActive = false;

        var finalTable = others.Count == 1;
        if (finalTable)
            others[0].IsFinalTable = true;

        var summary = moves.Count == 0
            ? $"{brokenName} quebrada (sem jogadores)"
            : $"{brokenName} quebrada: " + string.Join("; ", moves.Select(m => $"{m.PlayerName} → {m.ToTableName}, lugar {m.ToSeat}"));
        if (finalTable)
            summary += $". {EntryService.TableDisplayName(others[0])} agora é a mesa final";
        if (summary.Length > 1000)
            summary = summary[..997] + "...";

        _db.TournamentActivities.Add(new TournamentActivity
        {
            TournamentId = tournamentId,
            Type = nameof(TournamentActivityType.TableBroken),
            Description = summary,
            CreatedBy = userId
        });

        await _db.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);

        return Ok(new BreakTableResponse
        {
            BrokenTableName = brokenName,
            Moves = moves,
            FinalTable = finalTable
        });
    }

    private static bool IsPlaying(string status) =>
        status == nameof(EntryStatus.Active) || status == nameof(EntryStatus.Registered);

    private static string SeatText(TournamentTable? table, int? seat) =>
        table is null || seat is null
            ? "sem mesa"
            : $"{EntryService.TableDisplayName(table)}, lugar {seat}";

    private static string Plural(int n, string singular, string plural) =>
        $"{n} {(n == 1 ? singular : plural)}";

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(claim!.Value);
    }

    [HttpPost("unseat")]
    public async Task<ActionResult> Unseat(
        Guid tournamentId, [FromBody] UnseatPlayerRequest request, CancellationToken ct)
    {
        var entry = await _db.TournamentEntries
            .FirstOrDefaultAsync(e => e.Id == request.EntryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        // Remove o jogador da mesa SEM eliminá-lo (continua ativo, mas sem assento)
        entry.TableId = null;
        entry.SeatNumber = null;

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Jogador removido da mesa." });
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
