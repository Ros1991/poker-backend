using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

public class EntryService
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;
    private readonly IWhatsAppProvider _whatsApp;

    public EntryService(IAppDbContext db, IMapper mapper, IWhatsAppProvider whatsApp)
    {
        _db = db;
        _mapper = mapper;
        _whatsApp = whatsApp;
    }

    public async Task<List<EntryResponse>> GetEntriesAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var entries = await _db.TournamentEntries
            .Include(e => e.Person)
            .Where(e => e.TournamentId == tournamentId)
            .OrderBy(e => e.RegisteredAt)
            .ToListAsync(ct);

        return _mapper.Map<List<EntryResponse>>(entries);
    }

    public async Task DeleteEntryAsync(
        Guid tournamentId, Guid entryId, CancellationToken ct = default)
    {
        var entry = await _db.TournamentEntries
            .Include(e => e.Transactions)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        // Remover transações relacionadas
        if (entry.Transactions != null && entry.Transactions.Any())
            _db.Transactions.RemoveRange(entry.Transactions);

        _db.TournamentEntries.Remove(entry);

        // Atualizar contadores do torneio
        var tournament = await _db.Tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId, ct);
        if (tournament != null)
        {
            tournament.TotalEntries = Math.Max(0, tournament.TotalEntries - 1);
            tournament.PlayersRemaining = Math.Max(0, tournament.PlayersRemaining - 1);
            tournament.TotalPrizePool = Math.Max(0, tournament.TotalPrizePool - entry.TotalDue);
            tournament.NetPrizePool = tournament.TotalPrizePool - tournament.TotalCosts;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<EntryResponse> RegisterEntryAsync(
        Guid tournamentId, CreateEntryRequest request, Guid? registeredBy = null, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (tournament.Status != nameof(TournamentStatus.Draft)
            && tournament.Status != nameof(TournamentStatus.OpenForRegistration)
            && tournament.Status != nameof(TournamentStatus.InProgress))
            throw new DomainException("Torneio não está aberto para inscrições.");

        // Verificar late registration
        if (tournament.Status == nameof(TournamentStatus.InProgress)
            && tournament.LateRegistrationLevel.HasValue
            && tournament.CurrentLevel > tournament.LateRegistrationLevel)
            throw new DomainException("Período de late registration encerrado.");

        var person = await _db.Persons
            .FirstOrDefaultAsync(p => p.Id == request.PersonId, ct)
            ?? throw new DomainException("Pessoa não encontrada.");

        // Verificar se já tem entry (inclusive eliminados - não permitir re-entry)
        var existing = await _db.TournamentEntries
            .AnyAsync(e => e.TournamentId == tournamentId
                        && e.PersonId == request.PersonId, ct);

        if (existing)
            throw new DomainException("Jogador já está inscrito neste torneio.");

        var entryNumber = await _db.TournamentEntries
            .CountAsync(e => e.TournamentId == tournamentId
                          && e.PersonId == request.PersonId, ct) + 1;

        var entry = new TournamentEntry
        {
            TournamentId = tournamentId,
            PersonId = request.PersonId,
            Status = nameof(EntryStatus.Active),
            BuyInPaid = request.BuyInPaid,
            BuyInAmount = tournament.BuyInAmount,
            RebuyCount = 0,
            RebuyTotal = 0,
            AddonPurchased = false,
            AddonAmount = 0,
            TotalDue = tournament.BuyInAmount,
            TotalPaid = request.BuyInPaid ? tournament.BuyInAmount : 0,
            Balance = request.BuyInPaid ? 0 : tournament.BuyInAmount,
            PaymentStatus = request.BuyInPaid
                ? nameof(Domain.Enums.PaymentStatus.Paid)
                : nameof(Domain.Enums.PaymentStatus.Pending),
            EntryNumber = entryNumber,
            IsReentry = entryNumber > 1,
            RegisteredAt = DateTimeOffset.UtcNow,
            RegisteredBy = registeredBy
        };

        _db.TournamentEntries.Add(entry);

        // Registrar transação de buy-in
        _db.Transactions.Add(new Transaction
        {
            TournamentId = tournamentId,
            EntryId = entry.Id,
            Type = nameof(TransactionType.BuyIn),
            Amount = tournament.BuyInAmount,
            Description = $"Buy-in - {person.FullName}",
            CreatedBy = registeredBy
        });

        // Atualizar contadores do torneio
        tournament.TotalEntries++;
        tournament.PlayersRemaining++;
        tournament.TotalPrizePool += tournament.BuyInAmount;

        await _db.SaveChangesAsync(ct);

        entry.Person = person;
        return _mapper.Map<EntryResponse>(entry);
    }

    public async Task<EntryResponse> RegisterRebuyAsync(
        Guid tournamentId, Guid entryId, RebuyRequest request, Guid? registeredBy = null, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (tournament.Status != nameof(TournamentStatus.Draft)
            && tournament.Status != nameof(TournamentStatus.OpenForRegistration)
            && tournament.Status != nameof(TournamentStatus.InProgress))
            throw new DomainException("Não é possível registrar rebuy nesta fase do torneio.");

        if (!tournament.RebuyAmount.HasValue || tournament.RebuyAmount <= 0)
            throw new DomainException("Rebuy não está habilitado neste torneio.");

        if (tournament.RebuyUntilLevel.HasValue && tournament.CurrentLevel > tournament.RebuyUntilLevel)
            throw new DomainException("Período de rebuy encerrado.");

        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        if (entry.Status != nameof(EntryStatus.Active))
            throw new DomainException("Jogador não está ativo no torneio.");

        if (tournament.MaxRebuys.HasValue && entry.RebuyCount + request.Quantity > tournament.MaxRebuys)
            throw new DomainException($"Limite de rebuys atingido ({tournament.MaxRebuys}).");

        entry.RebuyCount += request.Quantity;
        entry.RebuyTotal = entry.RebuyCount * (tournament.RebuyAmount ?? 0);

        RecalculateBalance(entry, tournament);

        for (int i = 0; i < request.Quantity; i++)
        {
            _db.Transactions.Add(new Transaction
            {
                TournamentId = tournamentId,
                EntryId = entry.Id,
                Type = nameof(TransactionType.Rebuy),
                Amount = tournament.RebuyAmount!.Value,
                Description = $"Rebuy {entry.RebuyCount - request.Quantity + i + 1} - {entry.Person.FullName}",
                CreatedBy = registeredBy
            });
        }

        tournament.TotalRebuys += request.Quantity;
        tournament.TotalPrizePool += (tournament.RebuyAmount ?? 0) * request.Quantity;

        await _db.SaveChangesAsync(ct);
        return _mapper.Map<EntryResponse>(entry);
    }

    public async Task<EntryResponse> RegisterAddonAsync(
        Guid tournamentId, Guid entryId, Guid? registeredBy = null, CancellationToken ct = default, bool isDouble = false)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (tournament.Status != nameof(TournamentStatus.Draft)
            && tournament.Status != nameof(TournamentStatus.OpenForRegistration)
            && tournament.Status != nameof(TournamentStatus.InProgress))
            throw new DomainException("Não é possível registrar addon nesta fase do torneio.");

        if (!tournament.AddonAllowed || !tournament.AddonAmount.HasValue || tournament.AddonAmount <= 0)
            throw new DomainException("Addon não está habilitado neste torneio.");

        if (isDouble && !tournament.AddonDoubleAllowed)
            throw new DomainException("Addon duplo não está habilitado neste torneio.");

        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        if (entry.Status != nameof(EntryStatus.Active))
            throw new DomainException("Jogador não está ativo no torneio.");

        if (entry.AddonPurchased)
            throw new DomainException("Jogador já realizou addon.");

        var multiplier = isDouble ? 2 : 1;
        var addonValue = tournament.AddonAmount!.Value * multiplier;

        entry.AddonPurchased = true;
        entry.AddonDouble = isDouble;
        entry.AddonAmount = addonValue;

        RecalculateBalance(entry, tournament);

        var addonLabel = isDouble ? "Addon Duplo" : "Addon";
        _db.Transactions.Add(new Transaction
        {
            TournamentId = tournamentId,
            EntryId = entry.Id,
            Type = nameof(TransactionType.Addon),
            Amount = addonValue,
            Description = $"{addonLabel} - {entry.Person.FullName}",
            CreatedBy = registeredBy
        });

        tournament.TotalAddons++;
        tournament.TotalPrizePool += addonValue;

        await _db.SaveChangesAsync(ct);
        return _mapper.Map<EntryResponse>(entry);
    }

    public async Task<EntryResponse> RemoveRebuyAsync(
        Guid tournamentId, Guid entryId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        if (entry.RebuyCount <= 0)
            throw new DomainException("Jogador não tem rebuys para remover.");

        var rebuyValue = tournament.RebuyAmount ?? 0;

        // Remover a transação de rebuy mais recente
        var lastRebuyTx = await _db.Transactions
            .Where(t => t.EntryId == entryId && t.Type == nameof(TransactionType.Rebuy))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (lastRebuyTx != null)
            _db.Transactions.Remove(lastRebuyTx);

        entry.RebuyCount--;
        entry.RebuyTotal = entry.RebuyCount * rebuyValue;
        RecalculateBalance(entry, tournament);

        tournament.TotalRebuys = Math.Max(0, tournament.TotalRebuys - 1);
        tournament.TotalPrizePool = Math.Max(0, tournament.TotalPrizePool - rebuyValue);

        await _db.SaveChangesAsync(ct);
        return _mapper.Map<EntryResponse>(entry);
    }

    public async Task<EntryResponse> RemoveAddonAsync(
        Guid tournamentId, Guid entryId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        if (!entry.AddonPurchased)
            throw new DomainException("Jogador não possui addon.");

        var addonValue = entry.AddonAmount;

        // Remover a transação de addon
        var addonTx = await _db.Transactions
            .Where(t => t.EntryId == entryId && t.Type == nameof(TransactionType.Addon))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (addonTx != null)
            _db.Transactions.Remove(addonTx);

        entry.AddonPurchased = false;
        entry.AddonDouble = false;
        entry.AddonAmount = 0;
        RecalculateBalance(entry, tournament);

        tournament.TotalAddons = Math.Max(0, tournament.TotalAddons - 1);
        tournament.TotalPrizePool = Math.Max(0, tournament.TotalPrizePool - addonValue);

        await _db.SaveChangesAsync(ct);
        return _mapper.Map<EntryResponse>(entry);
    }

    public async Task<EliminationResultResponse> EliminatePlayerAsync(
        Guid tournamentId, Guid entryId, EliminateRequest request, Guid? createdBy = null, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            var tournament = await _db.Tournaments
                .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
                ?? throw new DomainException("Torneio não encontrado.");

            var entry = await _db.TournamentEntries
                .Include(e => e.Person)
                .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
                ?? throw new DomainException("Inscrição não encontrada.");

            if (entry.Status != nameof(EntryStatus.Active))
                throw new DomainException("Jogador não está ativo no torneio.");

            // Calcular posição
            var activePlayers = await _db.TournamentEntries
                .CountAsync(e => e.TournamentId == tournamentId
                              && e.Status == nameof(EntryStatus.Active), ct);

            var position = activePlayers;

            // Registrar eliminação
            var elimination = new Elimination
            {
                TournamentId = tournamentId,
                EntryId = entryId,
                EliminatedByEntryId = request.EliminatedByEntryId,
                TableId = entry.TableId,
                Position = position,
                BlindLevel = tournament.CurrentLevel,
                EliminatedAt = DateTimeOffset.UtcNow,
                Notes = request.Notes,
                CreatedBy = createdBy
            };
            _db.Eliminations.Add(elimination);

            // Atualizar entry
            entry.Status = nameof(EntryStatus.Eliminated);
            entry.FinalPosition = position;
            entry.EliminatedAt = DateTimeOffset.UtcNow;
            entry.EliminatedById = request.EliminatedByEntryId;
            entry.EliminationTableId = entry.TableId;
            entry.EliminationSeatNumber = entry.SeatNumber;
            entry.TableId = null;
            entry.SeatNumber = null;

            // Atualizar contadores
            tournament.PlayersRemaining = activePlayers - 1;

            // Se restou 1 jogador: ele é o campeão
            if (tournament.PlayersRemaining == 1)
            {
                var champion = await _db.TournamentEntries
                    .FirstOrDefaultAsync(e => e.TournamentId == tournamentId
                                           && e.Status == nameof(EntryStatus.Active), ct);

                if (champion is not null)
                {
                    champion.FinalPosition = 1;
                    champion.Status = nameof(EntryStatus.Awarded);
                    tournament.PlayersRemaining = 0;

                    // Finalizar torneio automaticamente quando sobra 1 jogador
                    tournament.Status = nameof(TournamentStatus.Finished);
                    tournament.IsTimerRunning = false;
                }
            }

            // Auditoria
            _db.AuditLogs.Add(new AuditLog
            {
                EntityType = "Elimination",
                EntityId = elimination.Id,
                Action = "Create",
                NewValues = System.Text.Json.JsonSerializer.Serialize(new
                {
                    playerId = entry.PersonId,
                    playerName = entry.Person.FullName,
                    position,
                    eliminatedBy = request.EliminatedByEntryId,
                    blindLevel = tournament.CurrentLevel
                }),
                UserId = createdBy,
                TournamentId = tournamentId,
                CreatedAt = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // Verificar ITM
            var prizeCount = await _db.TournamentPrizes
                .CountAsync(p => p.TournamentId == tournamentId, ct);

            // Gerar WhatsApp link
            string? whatsAppLink = null;
            if (!string.IsNullOrEmpty(entry.Person.Whatsapp))
            {
                var message = _whatsApp.BuildMessage("elimination", new Dictionary<string, string>
                {
                    ["name"] = entry.Person.FullName,
                    ["position"] = position.ToString(),
                    ["tournament"] = tournament.Name
                });
                whatsAppLink = _whatsApp.GenerateWhatsAppLink(entry.Person.Whatsapp, message);
            }

            // Obter nome do eliminador
            string? eliminatedByName = null;
            if (request.EliminatedByEntryId.HasValue)
            {
                var eliminator = await _db.TournamentEntries
                    .Include(e => e.Person)
                    .FirstOrDefaultAsync(e => e.Id == request.EliminatedByEntryId, ct);
                eliminatedByName = eliminator?.Person.FullName;
            }

            return new EliminationResultResponse
            {
                Position = position,
                PlayerName = entry.Person.FullName,
                EliminatedBy = eliminatedByName,
                PlayersRemaining = tournament.PlayersRemaining,
                IsInTheMoney = position <= prizeCount,
                WhatsAppLink = whatsAppLink
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<EntryResponse> UndoEliminationAsync(
        Guid tournamentId, Guid entryId, Guid? correctedBy = null, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        if (entry.Status != nameof(EntryStatus.Eliminated))
            throw new DomainException("Jogador não está eliminado.");

        // Encontrar a eliminação mais recente
        var elimination = await _db.Eliminations
            .Where(e => e.EntryId == entryId && !e.Corrected)
            .OrderByDescending(e => e.EliminatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Eliminação não encontrada.");

        elimination.Corrected = true;
        elimination.CorrectedAt = DateTimeOffset.UtcNow;
        elimination.CorrectedBy = correctedBy;
        elimination.CorrectionReason = "Eliminação desfeita pelo organizador";

        entry.Status = nameof(EntryStatus.Active);
        entry.FinalPosition = null;
        entry.EliminatedAt = null;
        entry.EliminatedById = null;

        // Restaurar mesa/posicao se ainda estiver disponivel
        if (entry.EliminationTableId.HasValue && entry.EliminationSeatNumber.HasValue)
        {
            var seatTaken = await _db.TournamentEntries
                .AnyAsync(e => e.TableId == entry.EliminationTableId
                            && e.SeatNumber == entry.EliminationSeatNumber
                            && e.Id != entry.Id, ct);
            if (!seatTaken)
            {
                entry.TableId = entry.EliminationTableId;
                entry.SeatNumber = entry.EliminationSeatNumber;
            }
            else
            {
                // Achar primeiro assento livre na mesma mesa
                var table = await _db.TournamentTables
                    .FirstOrDefaultAsync(t => t.Id == entry.EliminationTableId, ct);
                if (table != null)
                {
                    var taken = await _db.TournamentEntries
                        .Where(e => e.TableId == table.Id && e.SeatNumber != null)
                        .Select(e => e.SeatNumber!.Value)
                        .ToListAsync(ct);
                    for (int s = 1; s <= table.MaxSeats; s++)
                    {
                        if (!taken.Contains(s))
                        {
                            entry.TableId = table.Id;
                            entry.SeatNumber = s;
                            break;
                        }
                    }
                }
            }
        }

        entry.EliminationTableId = null;
        entry.EliminationSeatNumber = null;

        // Reverter o "campeão automático": se sobrava só 1 (Awarded), volta para Active
        var awardedChampion = await _db.TournamentEntries
            .FirstOrDefaultAsync(e => e.TournamentId == tournamentId
                                   && e.Status == nameof(EntryStatus.Awarded)
                                   && e.Id != entryId, ct);
        if (awardedChampion != null)
        {
            awardedChampion.Status = nameof(EntryStatus.Active);
            awardedChampion.FinalPosition = null;
        }

        tournament.PlayersRemaining++;

        await _db.SaveChangesAsync(ct);
        return _mapper.Map<EntryResponse>(entry);
    }

    private static void RecalculateBalance(TournamentEntry entry, Tournament tournament)
    {
        entry.TotalDue = entry.BuyInAmount
            + entry.RebuyTotal
            + (entry.AddonPurchased ? entry.AddonAmount : 0);

        entry.Balance = entry.TotalDue - entry.TotalPaid;

        entry.PaymentStatus = entry.Balance switch
        {
            0 => nameof(Domain.Enums.PaymentStatus.Paid),
            > 0 when entry.TotalPaid > 0 => nameof(Domain.Enums.PaymentStatus.PartiallyPaid),
            < 0 => nameof(Domain.Enums.PaymentStatus.Overpaid),
            _ => nameof(Domain.Enums.PaymentStatus.Pending)
        };
    }
}
