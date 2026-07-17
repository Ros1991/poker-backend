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
            if (entry.Status == nameof(EntryStatus.Active) || entry.Status == nameof(EntryStatus.Registered))
                tournament.PlayersRemaining = Math.Max(0, tournament.PlayersRemaining - 1);
            tournament.TotalPrizePool = Math.Max(0, tournament.TotalPrizePool - entry.TotalDue);

            // Feature staff/ranking: decrementa os custos automáticos por entrada
            await BumpAutoCostAsync(tournamentId, "Staff", -(tournament.StaffAmount ?? 0m), ct);
            await BumpAutoCostAsync(tournamentId, "RankingAccumulated",
                tournament.RankingContribMode == "PerPlayer" ? -(tournament.RankingContribValue ?? 0m) : 0m, ct);
            await RecalcAutoCostsAndTotals(tournament, ct);
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

        // Feature staff/ranking: o jogador paga buy-in + staff + ranking(fixo).
        // No modo % o ranking não é cobrado por jogador (sai do bolo no recálculo).
        var staffPart = tournament.StaffAmount ?? 0m;
        var rankingFixedPart = tournament.RankingContribMode == "PerPlayer"
            ? (tournament.RankingContribValue ?? 0m)
            : 0m;
        var entryCost = tournament.BuyInAmount + staffPart + rankingFixedPart;

        var entry = new TournamentEntry
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            PersonId = request.PersonId,
            Status = nameof(EntryStatus.Active),
            BuyInPaid = request.BuyInPaid,
            BuyInAmount = tournament.BuyInAmount,
            RebuyCount = 0,
            RebuyTotal = 0,
            AddonPurchased = false,
            AddonAmount = 0,
            TotalDue = entryCost,
            TotalPaid = request.BuyInPaid ? entryCost : 0,
            Balance = request.BuyInPaid ? 0 : entryCost,
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

        // Atualizar contadores do torneio (receita = tudo que entra)
        tournament.TotalEntries++;
        tournament.PlayersRemaining++;
        tournament.TotalPrizePool += entryCost;

        // Acumular staff e ranking-fixo (custos automáticos crescem por entrada)
        await BumpAutoCostAsync(tournamentId, "Staff", staffPart, ct);
        await BumpAutoCostAsync(tournamentId, "RankingAccumulated", rankingFixedPart, ct);
        await RecalcAutoCostsAndTotals(tournament, ct);

        await _db.SaveChangesAsync(ct);

        entry.Person = person;
        return _mapper.Map<EntryResponse>(entry);
    }

    /// <summary>
    /// Inscreve vários jogadores de uma vez (multi-seleção). Atômico (transação).
    /// Após criar as inscrições, se o torneio JÁ tiver mesas sorteadas, auto-aloca
    /// os novos jogadores (preenche vagas; se encher tudo, cria mesa e rebalanceia).
    /// </summary>
    public async Task<List<EntryResponse>> RegisterEntriesBulkAsync(
        Guid tournamentId, BulkCreateEntriesRequest request, Guid? registeredBy = null, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        if (tournament.Status != nameof(TournamentStatus.Draft)
            && tournament.Status != nameof(TournamentStatus.OpenForRegistration)
            && tournament.Status != nameof(TournamentStatus.InProgress))
            throw new DomainException("Torneio não está aberto para inscrições.");

        if (tournament.Status == nameof(TournamentStatus.InProgress)
            && tournament.LateRegistrationLevel.HasValue
            && tournament.CurrentLevel > tournament.LateRegistrationLevel)
            throw new DomainException("Período de late registration encerrado.");

        var personIds = request.PersonIds.Distinct().ToList();
        if (personIds.Count == 0)
            throw new DomainException("Nenhum jogador selecionado.");

        // Ignora quem já está inscrito (qualquer status) e ids inexistentes.
        var enrolledSet = (await _db.TournamentEntries
            .Where(e => e.TournamentId == tournamentId && personIds.Contains(e.PersonId))
            .Select(e => e.PersonId)
            .ToListAsync(ct)).ToHashSet();

        var personMap = (await _db.Persons
            .Where(p => personIds.Contains(p.Id))
            .ToListAsync(ct)).ToDictionary(p => p.Id);

        var toCreate = personIds
            .Where(id => !enrolledSet.Contains(id) && personMap.ContainsKey(id))
            .ToList();

        if (toCreate.Count == 0)
            throw new DomainException("Todos os jogadores selecionados já estão inscritos.");

        // Feature staff/ranking: jogador paga buy-in + staff + ranking(fixo).
        var staffPart = tournament.StaffAmount ?? 0m;
        var rankingFixedPart = tournament.RankingContribMode == "PerPlayer"
            ? (tournament.RankingContribValue ?? 0m)
            : 0m;
        var entryCost = tournament.BuyInAmount + staffPart + rankingFixedPart;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var createdEntries = new List<TournamentEntry>();
        foreach (var personId in toCreate)
        {
            var person = personMap[personId];
            var entry = new TournamentEntry
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                PersonId = personId,
                Status = nameof(EntryStatus.Active),
                BuyInPaid = request.BuyInPaid,
                BuyInAmount = tournament.BuyInAmount,
                RebuyCount = 0,
                RebuyTotal = 0,
                AddonPurchased = false,
                AddonAmount = 0,
                TotalDue = entryCost,
                TotalPaid = request.BuyInPaid ? entryCost : 0,
                Balance = request.BuyInPaid ? 0 : entryCost,
                PaymentStatus = request.BuyInPaid
                    ? nameof(Domain.Enums.PaymentStatus.Paid)
                    : nameof(Domain.Enums.PaymentStatus.Pending),
                EntryNumber = 1,
                IsReentry = false,
                RegisteredAt = DateTimeOffset.UtcNow,
                RegisteredBy = registeredBy
            };
            _db.TournamentEntries.Add(entry);

            _db.Transactions.Add(new Transaction
            {
                TournamentId = tournamentId,
                EntryId = entry.Id,
                Type = nameof(TransactionType.BuyIn),
                Amount = tournament.BuyInAmount,
                Description = $"Buy-in - {person.FullName}",
                CreatedBy = registeredBy
            });

            tournament.TotalEntries++;
            tournament.PlayersRemaining++;
            tournament.TotalPrizePool += entryCost;

            entry.Person = person;
            createdEntries.Add(entry);
        }

        // Acumula staff e ranking-fixo uma vez (custos automáticos crescem por entrada).
        await BumpAutoCostAsync(tournamentId, "Staff", staffPart * toCreate.Count, ct);
        await BumpAutoCostAsync(tournamentId, "RankingAccumulated", rankingFixedPart * toCreate.Count, ct);
        await RecalcAutoCostsAndTotals(tournament, ct);

        await _db.SaveChangesAsync(ct);

        // Auto-alocação em mesas — somente se já existir pelo menos 1 mesa.
        await AutoSeatNewEntriesAsync(tournament, createdEntries, ct);

        await tx.CommitAsync(ct);

        return createdEntries.Select(e => _mapper.Map<EntryResponse>(e)).ToList();
    }

    /// <summary>
    /// Auto-aloca os novos jogadores nas mesas existentes. Regra: NÃO faz nada se
    /// ainda não houver mesa criada (o sorteio acontece depois pelo fluxo normal).
    /// Preenche a mesa com vaga e menor lotação; se todas estiverem cheias, cria a
    /// próxima mesa e rebalanceia sorteando quem muda até diferença ≤ 1.
    /// </summary>
    private async Task AutoSeatNewEntriesAsync(
        Tournament tournament, List<TournamentEntry> newEntries, CancellationToken ct)
    {
        var tables = await _db.TournamentTables
            .Where(t => t.TournamentId == tournament.Id && t.IsActive)
            .OrderBy(t => t.TableNumber)
            .ToListAsync(ct);

        if (tables.Count == 0)
            return; // mesas ainda não sorteadas → deixa pro sorteio posterior

        var newTableSeats = tournament.SeatsPerTable > 1 ? tournament.SeatsPerTable : 9;

        // Ocupação atual: jogadores ATIVOS já assentados, por mesa.
        var seatedActive = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournament.Id
                     && e.Status == nameof(EntryStatus.Active)
                     && e.TableId != null)
            .ToListAsync(ct);

        var occupancy = tables.ToDictionary(
            t => t.Id,
            t => seatedActive.Where(e => e.TableId == t.Id).ToList());

        var nextTableNumber = tables.Max(t => t.TableNumber) + 1;

        foreach (var entry in newEntries)
        {
            // Mesa com assento livre (respeitando o MaxSeats da própria mesa) e menor lotação.
            var target = tables
                .Where(t => occupancy[t.Id].Count < t.MaxSeats)
                .OrderBy(t => occupancy[t.Id].Count)
                .FirstOrDefault();

            if (target is null)
            {
                // Todas cheias → cria a próxima mesa e rebalanceia (sorteando quem muda).
                target = new TournamentTable
                {
                    TournamentId = tournament.Id,
                    TableNumber = nextTableNumber,
                    TableName = $"Mesa {nextTableNumber}",
                    MaxSeats = newTableSeats,
                    IsActive = true
                };
                nextTableNumber++;
                _db.TournamentTables.Add(target);
                await _db.SaveChangesAsync(ct); // garante o Id real da nova mesa

                tables.Add(target);
                occupancy[target.Id] = [];

                RebalanceRandom(tables, occupancy);
                target = tables.OrderBy(t => occupancy[t.Id].Count).First();
            }

            // Assento = próximo livre na mesa (max+1); não mexe nos assentos de quem já está.
            entry.TableId = target.Id;
            entry.SeatNumber = NextSeat(occupancy[target.Id]);
            occupancy[target.Id].Add(entry);
        }

        await _db.SaveChangesAsync(ct);
    }

    private static int NextSeat(List<TournamentEntry> seated) =>
        seated.Count == 0 ? 1 : seated.Max(e => e.SeatNumber ?? 0) + 1;

    /// <summary>
    /// Move jogadores sorteados da mesa mais cheia para a mais vazia até a diferença ser ≤ 1.
    /// Quem é movido recebe o próximo assento livre no destino (sem colidir com quem já está lá).
    /// </summary>
    private static void RebalanceRandom(
        List<TournamentTable> tables, Dictionary<Guid, List<TournamentEntry>> occupancy)
    {
        while (true)
        {
            var max = tables.OrderByDescending(t => occupancy[t.Id].Count).First();
            var min = tables.OrderBy(t => occupancy[t.Id].Count).First();
            if (occupancy[max.Id].Count - occupancy[min.Id].Count <= 1)
                break;

            var fromList = occupancy[max.Id];
            var idx = Random.Shared.Next(fromList.Count);
            var mover = fromList[idx];
            fromList.RemoveAt(idx);
            mover.TableId = min.Id;
            mover.SeatNumber = NextSeat(occupancy[min.Id]);
            occupancy[min.Id].Add(mover);
        }
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

        if (tournament.MaxRebuys.HasValue && tournament.MaxRebuys > 0 && entry.RebuyCount + request.Quantity > tournament.MaxRebuys)
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
        await RecalcAutoCostsAndTotals(tournament, ct);

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
        await RecalcAutoCostsAndTotals(tournament, ct);

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
        await RecalcAutoCostsAndTotals(tournament, ct);

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
        await RecalcAutoCostsAndTotals(tournament, ct);

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

            if (tournament.Status == nameof(TournamentStatus.Finished))
                throw new DomainException("Torneio já está finalizado.");

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

            // ITM: a posição é premiada? Usa nº de prêmios confirmados; se ainda não confirmou,
            // estima por SuggestPrizeCount para o ITM ser consistente durante o jogo.
            var confirmedPrizeCount = await _db.TournamentPrizes
                .CountAsync(p => p.TournamentId == tournamentId, ct);
            var effectivePrizeCount = confirmedPrizeCount > 0
                ? confirmedPrizeCount
                : PrizeService.SuggestPrizeCount(tournament.TotalEntries);
            var isItm = position <= effectivePrizeCount;

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

            // Atualizar entry — jogador ITM vira Awarded (premiado), não apenas Eliminated
            entry.Status = isItm ? nameof(EntryStatus.Awarded) : nameof(EntryStatus.Eliminated);
            entry.FinalPosition = position;
            entry.EliminatedAt = DateTimeOffset.UtcNow;
            entry.EliminatedById = request.EliminatedByEntryId;
            entry.EliminationTableId = entry.TableId;
            entry.EliminationSeatNumber = entry.SeatNumber;
            entry.TableId = null;
            entry.SeatNumber = null;

            // Vincular o prêmio confirmado a este jogador ITM (se houver premiação confirmada)
            if (isItm && confirmedPrizeCount > 0)
            {
                var wonPrize = await _db.TournamentPrizes
                    .FirstOrDefaultAsync(p => p.TournamentId == tournamentId && p.Position == position, ct);
                if (wonPrize != null)
                {
                    wonPrize.EntryId = entry.Id;
                    entry.PrizeAmount = wonPrize.Amount;
                }
            }

            // Atualizar contadores
            tournament.PlayersRemaining = activePlayers - 1;

            // Se restou 1 jogador: ele é o campeão
            if (tournament.PlayersRemaining == 1)
            {
                // O status Awarded/Eliminated do vice ainda não foi salvo — no banco ele
                // segue Active, então a query PRECISA excluir o próprio eliminado.
                var champion = await _db.TournamentEntries
                    .FirstOrDefaultAsync(e => e.TournamentId == tournamentId
                                           && e.Status == nameof(EntryStatus.Active)
                                           && e.Id != entryId, ct);

                if (champion is not null)
                {
                    champion.FinalPosition = 1;
                    champion.Status = nameof(EntryStatus.Awarded);
                    tournament.PlayersRemaining = 0;

                    // Vincular o prêmio do 1º lugar ao campeão (se confirmado)
                    if (confirmedPrizeCount > 0)
                    {
                        var firstPrize = await _db.TournamentPrizes
                            .FirstOrDefaultAsync(p => p.TournamentId == tournamentId && p.Position == 1, ct);
                        if (firstPrize != null)
                        {
                            firstPrize.EntryId = champion.Id;
                            champion.PrizeAmount = firstPrize.Amount;
                        }
                    }

                    // Finalizar torneio automaticamente quando sobra 1 jogador
                    tournament.Status = nameof(TournamentStatus.Finished);
                    tournament.IsTimerRunning = false;

                    // O auto-finish NÃO passa pelo UpdateStatusAsync, então o acúmulo do
                    // ranking precisa acontecer aqui (idempotente via RankingPrizeAccrued).
                    if (tournament.RankingId.HasValue && tournament.RankingPrizeAccrued is null)
                    {
                        var rankingCost = await _db.CostExtras.FirstOrDefaultAsync(
                            c => c.TournamentId == tournament.Id && c.CostType == "RankingAccumulated", ct);
                        var accrued = rankingCost?.Amount ?? 0m;
                        if (accrued > 0)
                        {
                            var ranking = await _db.Rankings.FirstOrDefaultAsync(
                                r => r.Id == tournament.RankingId.Value, ct);
                            if (ranking is not null)
                                ranking.AccumulatedPrize += accrued;
                        }
                        tournament.RankingPrizeAccrued = accrued;
                        tournament.RankingAccruedAt = DateTimeOffset.UtcNow;
                    }
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
                IsInTheMoney = isItm,
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

        if (entry.Status != nameof(EntryStatus.Eliminated)
            && entry.Status != nameof(EntryStatus.Awarded))
            throw new DomainException("Jogador não está eliminado nem premiado.");

        // Campeão nunca foi eliminado (EliminatedAt nulo): não há eliminação para desfazer.
        if (entry.EliminatedAt is null)
            throw new DomainException(
                "O campeão não foi eliminado por ninguém. Desfaça a eliminação do vice-campeão para reabrir o torneio.");

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
        entry.PrizeAmount = null;

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

        // Reverter o "campeão automático": ele nunca foi eliminado (EliminatedAt == null).
        var champion = await _db.TournamentEntries
            .FirstOrDefaultAsync(e => e.TournamentId == tournamentId
                                   && e.Status == nameof(EntryStatus.Awarded)
                                   && e.EliminatedAt == null
                                   && e.Id != entryId, ct);
        if (champion != null)
        {
            champion.Status = nameof(EntryStatus.Active);
            champion.FinalPosition = null;
            champion.PrizeAmount = null;
        }

        // Reabrir o torneio se estava finalizado e reverter o acúmulo no ranking.
        if (tournament.Status == nameof(TournamentStatus.Finished))
        {
            tournament.Status = nameof(TournamentStatus.InProgress);
            if (tournament.RankingId.HasValue && tournament.RankingPrizeAccrued is > 0)
            {
                var ranking = await _db.Rankings.FirstOrDefaultAsync(
                    r => r.Id == tournament.RankingId.Value, ct);
                if (ranking is not null)
                    ranking.AccumulatedPrize = Math.Max(0m, ranking.AccumulatedPrize - tournament.RankingPrizeAccrued.Value);
                tournament.RankingPrizeAccrued = null;
                tournament.RankingAccruedAt = null;
            }
        }

        // Recalcular posição/ITM/prêmio de TODOS os que já saíram (a posição de todos muda)
        // e define PlayersRemaining a partir dos ativos.
        await RecomputeStandingsAsync(tournament, ct);

        await _db.SaveChangesAsync(ct);
        return _mapper.Map<EntryResponse>(entry);
    }

    /// <summary>
    /// Recalcula posição final, ITM (premiado/eliminado) e vínculo de prêmio de TODOS os
    /// jogadores que já saíram, a partir da ordem de eliminação (último a sair = melhor
    /// posição entre os que saíram). Usado ao desfazer eliminação. Não rebaixa quem já foi
    /// pago (PaidOut). Define tournament.PlayersRemaining = nº de ativos.
    /// </summary>
    private async Task RecomputeStandingsAsync(Tournament tournament, CancellationToken ct)
    {
        var entries = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournament.Id)
            .ToListAsync(ct);

        var activeCount = entries.Count(e => e.Status == nameof(EntryStatus.Active));

        var finished = entries
            .Where(e => e.Status == nameof(EntryStatus.Eliminated)
                     || e.Status == nameof(EntryStatus.Awarded)
                     || e.Status == nameof(EntryStatus.PaidOut))
            .OrderByDescending(e => e.EliminatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        var prizes = await _db.TournamentPrizes
            .Where(p => p.TournamentId == tournament.Id)
            .ToListAsync(ct);
        var effectivePrizeCount = prizes.Count > 0
            ? prizes.Count
            : PrizeService.SuggestPrizeCount(tournament.TotalEntries);

        // Limpa vínculos de prêmio que não sejam de jogadores já pagos (religados abaixo).
        var paidIds = entries
            .Where(e => e.Status == nameof(EntryStatus.PaidOut))
            .Select(e => e.Id)
            .ToHashSet();
        foreach (var p in prizes)
            if (!p.EntryId.HasValue || !paidIds.Contains(p.EntryId.Value))
                p.EntryId = null;

        for (int i = 0; i < finished.Count; i++)
        {
            var e = finished[i];
            var position = activeCount + 1 + i;
            e.FinalPosition = position;

            if (e.Status == nameof(EntryStatus.PaidOut))
                continue; // já pago: mantém status e prêmio

            var isItm = position <= effectivePrizeCount;
            e.Status = isItm ? nameof(EntryStatus.Awarded) : nameof(EntryStatus.Eliminated);

            var prize = prizes.FirstOrDefault(p => p.Position == position);
            if (isItm && prize != null)
            {
                e.PrizeAmount = prize.Amount;
                prize.EntryId = e.Id;
            }
            else
            {
                e.PrizeAmount = null;
            }
        }

        tournament.PlayersRemaining = activeCount;
    }

    private static void RecalculateBalance(TournamentEntry entry, Tournament tournament)
    {
        // O devido inclui staff + ranking-fixo (mesma fórmula do cadastro), além de
        // buy-in, rebuys e addon. Sem isso, rebuy/addon zeravam o staff/ranking do saldo.
        var staffPart = tournament.StaffAmount ?? 0m;
        var rankingFixedPart = tournament.RankingContribMode == "PerPlayer"
            ? (tournament.RankingContribValue ?? 0m)
            : 0m;

        entry.TotalDue = entry.BuyInAmount
            + staffPart
            + rankingFixedPart
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

    // Ajusta (incrementa/decrementa) o valor de um custo automático (staff/ranking-fixo).
    private async Task BumpAutoCostAsync(Guid tournamentId, string costType, decimal delta, CancellationToken ct)
    {
        if (delta == 0m) return;
        var cost = await _db.CostExtras
            .FirstOrDefaultAsync(c => c.TournamentId == tournamentId && c.CostType == costType, ct);
        if (cost is not null)
            cost.Amount = Math.Max(0m, cost.Amount + delta);
    }

    // Recalcula o custo de ranking no modo % e os totais (TotalCosts/NetPrizePool) do torneio.
    private async Task RecalcAutoCostsAndTotals(Tournament tournament, CancellationToken ct)
    {
        var costs = await _db.CostExtras
            .Where(c => c.TournamentId == tournament.Id)
            .ToListAsync(ct);

        if (tournament.RankingContribMode == "Percent" && tournament.RankingContribValue is > 0)
        {
            var rankingCost = costs.FirstOrDefault(c => c.CostType == "RankingAccumulated");
            if (rankingCost is not null)
            {
                var staff = costs.Where(c => c.CostType == "Staff").Sum(c => c.Amount);
                var manual = costs
                    .Where(c => c.CostType != "Staff" && c.CostType != "RankingAccumulated")
                    .Sum(c => c.Amount);
                var baseForPct = tournament.TotalPrizePool - staff - manual;
                var raw = (tournament.RankingContribValue!.Value / 100m) * baseForPct;
                // arredonda ao múltiplo de 10 mais próximo (>= 0)
                rankingCost.Amount = Math.Max(0m, Math.Round(raw / 10m, MidpointRounding.AwayFromZero) * 10m);
            }
        }

        tournament.TotalCosts = costs.Sum(c => c.Amount);
        tournament.NetPrizePool = tournament.TotalPrizePool - tournament.TotalCosts;
    }
}
