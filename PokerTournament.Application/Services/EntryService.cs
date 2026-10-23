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

        await RemoveEntryAndAdjustTotalsAsync(tournamentId, entry, ct);

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Apaga a inscrição e as transações dela e atualiza os contadores do torneio (inscrições,
    /// jogadores, bolo, rebuys, add-ons) e os custos automáticos. Pagamentos/abatimentos ainda
    /// valendo que creditaram um custo têm o crédito desfeito (mesma regra do estorno), senão o
    /// custo ficaria pago sem transação que o sustente. Antes, solta as referências que travariam
    /// o delete: apaga as eliminações CORRIGIDAS do jogador (FK Restrict) e anula quem apontava ele
    /// como eliminador. Exige entry.Transactions carregado. Não salva — quem chama faz o SaveChanges.
    /// </summary>
    private async Task RemoveEntryAndAdjustTotalsAsync(
        Guid tournamentId, TournamentEntry entry, CancellationToken ct)
    {
        var creditedTransactions = entry.Transactions
            .Where(t => t.ReversedAt == null
                     && t.PixDestinationId.HasValue
                     && (t.Type == nameof(TransactionType.Payment) || t.Type == nameof(TransactionType.Settlement)))
            .ToList();
        foreach (var t in creditedTransactions)
        {
            var cost = await _db.CostExtras
                .FirstOrDefaultAsync(c => c.Id == t.PixDestinationId && c.TournamentId == tournamentId, ct);
            PaymentService.ReverseCostCredit(t, cost);
        }

        var correctedEliminations = await _db.Eliminations
            .Where(e => e.EntryId == entry.Id && e.Corrected)
            .ToListAsync(ct);
        _db.Eliminations.RemoveRange(correctedEliminations);

        var eliminatedByHim = await _db.Eliminations
            .Where(e => e.EliminatedByEntryId == entry.Id && e.EntryId != entry.Id)
            .ToListAsync(ct);
        foreach (var e in eliminatedByHim)
            e.EliminatedByEntryId = null;

        var entriesEliminatedByHim = await _db.TournamentEntries
            .Where(e => e.EliminatedById == entry.Id && e.Id != entry.Id)
            .ToListAsync(ct);
        foreach (var e in entriesEliminatedByHim)
            e.EliminatedById = null;

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
            tournament.TotalRebuys = Math.Max(0, tournament.TotalRebuys - entry.RebuyCount);
            if (entry.AddonPurchased)
                tournament.TotalAddons = Math.Max(0, tournament.TotalAddons - 1);

            // Feature staff/ranking: decrementa os custos automáticos por entrada
            await BumpAutoCostAsync(tournamentId, "Staff", -(tournament.StaffAmount ?? 0m), ct);
            await BumpAutoCostAsync(tournamentId, "RankingAccumulated",
                tournament.RankingContribMode == "PerPlayer" ? -(tournament.RankingContribValue ?? 0m) : 0m, ct);
            await RecalcAutoCostsAndTotals(tournament, ct);
        }
    }

    /// <summary>Mensagem única quando a inscrição ainda tem algo valendo (histórico e desfazer).</summary>
    public const string UndoRegistrationBlockedMessage =
        "Desfaça antes os rebuys, add-ons, pagamentos e a eliminação deste jogador.";

    public const string UndoFinishedTournamentMessage =
        "O torneio já terminou. Não dá para desfazer pelo terminal.";

    public const string UndoPrizePaidMessage =
        "Já há prêmio pago neste torneio. Desfaça pelo painel.";

    /// <summary>
    /// Desfazer rebuy/add-on/inscrição pelo histórico mexe no bolo: com o torneio encerrado ou
    /// prêmio já pago, o bolo cairia abaixo do que foi distribuído. Null = pode desfazer.
    /// (Pagamento/abatimento não mexem no bolo e continuam desfazíveis.)
    /// </summary>
    public static string? MoneyUndoBlockedReason(string tournamentStatus, bool anyPrizePaid) =>
        tournamentStatus == nameof(TournamentStatus.Finished) || tournamentStatus == nameof(TournamentStatus.Cancelled)
            ? UndoFinishedTournamentMessage
            : anyPrizePaid ? UndoPrizePaidMessage : null;

    /// <summary>Lança DomainException quando o histórico não pode desfazer rebuy/add-on/inscrição (ver MoneyUndoBlockedReason).</summary>
    public async Task EnsureMoneyUndoAllowedAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var status = await _db.Tournaments
            .Where(t => t.Id == tournamentId)
            .Select(t => t.Status)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var paidPrizeEntryIds = await PaidPrizeEntryIdsAsync(tournamentId, ct);
        var anyPrizePaid = paidPrizeEntryIds.Count > 0
            || await _db.TournamentPrizes.AnyAsync(p => p.TournamentId == tournamentId && p.Paid, ct)
            || await _db.TournamentEntries.AnyAsync(e => e.TournamentId == tournamentId
                                                      && (e.PrizePaid || e.Status == nameof(EntryStatus.PaidOut)), ct);

        var reason = MoneyUndoBlockedReason(status, anyPrizePaid);
        if (reason is not null)
            throw new DomainException(reason);
    }

    /// <summary>Inscrições ligadas a um prêmio já pago (TournamentPrize.Paid).</summary>
    public async Task<HashSet<Guid>> PaidPrizeEntryIdsAsync(Guid tournamentId, CancellationToken ct = default) =>
        (await _db.TournamentPrizes
            .Where(p => p.TournamentId == tournamentId && p.Paid && p.EntryId != null)
            .Select(p => p.EntryId!.Value)
            .ToListAsync(ct)).ToHashSet();

    /// <summary>Prêmio do jogador já pago: por qualquer das marcas que o pagamento grava.</summary>
    public static bool IsPrizePaid(TournamentEntry entry, ISet<Guid> paidPrizeEntryIds) =>
        entry.PrizePaid
        || entry.Status == nameof(EntryStatus.PaidOut)
        || paidPrizeEntryIds.Contains(entry.Id);

    public static string UndoEliminationPrizePaidMessage(TournamentEntry paidEntry) =>
        $"O prêmio de {PlayerDisplayName(paidEntry.Person)} já foi pago. Desfaça o pagamento do prêmio no painel antes.";

    /// <summary>
    /// Desfazer a eliminação de <paramref name="target"/> muda a posição dele e de todos que
    /// ficaram à frente (o campeão — EliminatedAt nulo — e quem saiu depois dele). Se algum
    /// desses (ou ele mesmo) já recebeu o prêmio, devolve essa inscrição: o desfazer fica bloqueado.
    /// Quem saiu antes dele mantém a posição. Exige Person carregado para a mensagem.
    /// </summary>
    public static TournamentEntry? FindPaidPrizeBlockingUndoElimination(
        TournamentEntry target, IEnumerable<TournamentEntry> entries, ISet<Guid> paidPrizeEntryIds)
    {
        if (IsPrizePaid(target, paidPrizeEntryIds))
            return target;

        return entries
            .Where(e => e.Id != target.Id
                     && (e.Status == nameof(EntryStatus.Eliminated)
                         || e.Status == nameof(EntryStatus.Awarded)
                         || e.Status == nameof(EntryStatus.PaidOut))
                     && (e.EliminatedAt == null
                         || (target.EliminatedAt.HasValue && e.EliminatedAt > target.EliminatedAt)
                         || (target.FinalPosition.HasValue && e.FinalPosition.HasValue
                             && e.FinalPosition < target.FinalPosition)))
            .OrderBy(e => e.FinalPosition ?? int.MaxValue)
            .FirstOrDefault(e => IsPrizePaid(e, paidPrizeEntryIds));
    }

    /// <summary>
    /// Serializa, por torneio, as operações que mexem em lugares (mover, quebrar, sortear,
    /// criar mesa, adicionar com assento, inscrição em lote, desfazer eliminação): sem isso,
    /// duas requisições leem o mesmo lugar livre e as duas sentam alguém nele (linhas diferentes,
    /// o xmin não pega). pg_advisory_xact_lock com chave tirada do id do torneio; tem que rodar
    /// DENTRO da transação explícita (solta no commit/rollback) e quem chama lê a ocupação DEPOIS.
    /// </summary>
    public static async Task LockSeatingAsync(IAppDbContext db, Guid tournamentId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A trava de lugares exige uma transação aberta.");

        // Chave bigint determinística: os 8 primeiros bytes do Guid do torneio
        var key = BitConverter.ToInt64(tournamentId.ToByteArray(), 0);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }

    /// <summary>
    /// Desfaz uma inscrição feita por engano (histórico do terminal). Só quando o jogador
    /// está ativo e não tem rebuy, add-on, pagamento/abatimento ou eliminação valendo.
    /// Remove a inscrição como o DeleteEntryAsync e registra a atividade PlayerRemoved.
    /// </summary>
    public async Task UndoRegistrationAsync(
        Guid tournamentId, Guid entryId, Guid? undoneBy = null, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var entry = await _db.TournamentEntries
            .Include(e => e.Transactions)
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        var hasLiveMoney = entry.Transactions.Any(t => t.ReversedAt == null
            && (t.Type == nameof(TransactionType.Rebuy)
                || t.Type == nameof(TransactionType.Addon)
                || t.Type == nameof(TransactionType.Payment)
                || t.Type == nameof(TransactionType.Settlement)));
        var hasLiveElimination = await _db.Eliminations
            .AnyAsync(e => e.EntryId == entryId && !e.Corrected, ct);

        if ((entry.Status != nameof(EntryStatus.Active) && entry.Status != nameof(EntryStatus.Registered))
            || hasLiveMoney || hasLiveElimination
            || entry.RebuyCount > 0 || entry.AddonPurchased)
            throw new DomainException(UndoRegistrationBlockedMessage);

        var playerName = PlayerDisplayName(entry.Person);

        await RemoveEntryAndAdjustTotalsAsync(tournamentId, entry, ct);

        _db.TournamentActivities.Add(new TournamentActivity
        {
            TournamentId = tournamentId,
            EntryId = entryId, // sem FK: a inscrição some, o registro fica
            Type = nameof(TournamentActivityType.PlayerRemoved),
            Description = $"Inscrição desfeita: {playerName}",
            CreatedBy = undoneBy
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Jogadores do home game do torneio que ainda não estão nele (só id, nome, apelido e foto).</summary>
    public async Task<List<AvailablePlayerResponse>> GetAvailablePlayersAsync(
        Guid tournamentId, CancellationToken ct = default)
    {
        var homeGameId = await _db.Tournaments
            .Where(t => t.Id == tournamentId)
            .Select(t => (Guid?)t.HomeGameId)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var players = await _db.Persons
            .Where(p => p.HomeGameId == homeGameId
                     && p.IsActive
                     && p.Type == "Jogador"
                     && !p.TournamentEntries.Any(e => e.TournamentId == tournamentId))
            .Select(p => new AvailablePlayerResponse
            {
                Id = p.Id,
                FullName = p.FullName,
                Nickname = p.Nickname,
                PhotoUrl = p.PhotoUrl
            })
            .ToListAsync(ct);

        return players
            .OrderBy(p => string.IsNullOrWhiteSpace(p.Nickname) ? p.FullName : p.Nickname,
                StringComparer.InvariantCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Adicionar jogador pelo terminal: jogador do home game (PersonId) ou cadastro rápido
    /// (nome e apelido). Inscreve com as regras de sempre (RegisterEntryAsync) e, se já houver
    /// mesas, senta sozinho na mesa com menos jogadores ativos (empate → menor número) num
    /// lugar livre aleatório. Tudo numa transação.
    /// </summary>
    public async Task<QuickAddEntryResponse> QuickAddAsync(
        Guid tournamentId, QuickAddEntryRequest request, Guid? registeredBy = null, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Senta sozinho: serializa com move/quebra/sorteio antes de ler a ocupação
        await LockSeatingAsync(_db, tournamentId, ct);

        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        Guid personId;
        if (request.PersonId.HasValue)
        {
            personId = request.PersonId.Value;
        }
        else
        {
            var fullName = NormalizeSpaces(request.FullName);
            if (fullName.Length < 2 || fullName.Length > 120)
                throw new DomainException("Informe o nome do jogador (de 2 a 120 caracteres).");

            var nickname = NormalizeSpaces(request.Nickname);
            if (nickname.Length > 100)
                throw new DomainException("O apelido pode ter no máximo 100 caracteres.");

            // Mesmo nome (ignorando maiúsculas e espaços) já cadastrado no home game → usar a lista
            var existingNames = await _db.Persons
                .Where(p => p.HomeGameId == tournament.HomeGameId)
                .Select(p => p.FullName)
                .ToListAsync(ct);
            var key = NameKey(fullName);
            if (existingNames.Any(n => NameKey(n) == key))
                throw new DomainException("Já existe um jogador com esse nome. Procure na lista.");

            var person = new Person
            {
                HomeGameId = tournament.HomeGameId,
                Type = "Jogador",
                FullName = fullName,
                Nickname = nickname.Length == 0 ? null : nickname
            };
            _db.Persons.Add(person);
            await _db.SaveChangesAsync(ct); // garante o Id real da pessoa
            personId = person.Id;
        }

        var created = await RegisterEntryAsync(
            tournamentId, new CreateEntryRequest { PersonId = personId, BuyInPaid = request.BuyInPaid },
            registeredBy, ct);

        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstAsync(e => e.Id == created.Id, ct);

        TournamentTable? seatedAt = null;
        var tables = await _db.TournamentTables
            .Where(t => t.TournamentId == tournamentId && t.IsActive)
            .OrderBy(t => t.TableNumber)
            .ToListAsync(ct);

        if (tables.Count > 0)
        {
            var tableIds = tables.Select(t => t.Id).ToList();
            var seated = await _db.TournamentEntries
                .Where(e => e.TournamentId == tournamentId
                         && e.Id != entry.Id
                         && (e.Status == nameof(EntryStatus.Active) || e.Status == nameof(EntryStatus.Registered))
                         && e.TableId != null
                         && tableIds.Contains(e.TableId.Value))
                .Select(e => new { TableId = e.TableId!.Value, e.SeatNumber })
                .ToListAsync(ct);

            var candidates = tables
                .Select(t =>
                {
                    var taken = seated.Where(s => s.TableId == t.Id && s.SeatNumber != null)
                        .Select(s => s.SeatNumber!.Value).ToHashSet();
                    var free = Enumerable.Range(1, t.MaxSeats).Where(s => !taken.Contains(s)).ToList();
                    return new { Table = t, Count = seated.Count(s => s.TableId == t.Id), Free = free };
                })
                // Lotação também conta: lugar "livre" numa mesa já com MaxSeats jogadores não serve
                .Where(c => c.Free.Count > 0 && c.Count < c.Table.MaxSeats)
                .OrderBy(c => c.Count)
                .ThenBy(c => c.Table.TableNumber)
                .FirstOrDefault();

            if (candidates is not null)
            {
                seatedAt = candidates.Table;
                entry.TableId = seatedAt.Id;
                entry.SeatNumber = candidates.Free[Random.Shared.Next(candidates.Free.Count)];

                _db.TournamentActivities.Add(new TournamentActivity
                {
                    TournamentId = tournamentId,
                    EntryId = entry.Id,
                    Type = nameof(TournamentActivityType.PlayerSeated),
                    Description = $"{PlayerDisplayName(entry.Person)} sentou na {TableDisplayName(seatedAt)}, lugar {entry.SeatNumber}",
                    CreatedBy = registeredBy
                });

                await _db.SaveChangesAsync(ct);
            }
        }

        await tx.CommitAsync(ct);

        var response = _mapper.Map<EntryResponse>(entry);
        // O terminal (inclusive o dealer por QR) nunca recebe contato/documento
        response.Person.Email = null;
        response.Person.Whatsapp = null;
        response.Person.Document = null;
        response.Person.Notes = null;

        return new QuickAddEntryResponse
        {
            Entry = response,
            TableId = seatedAt?.Id,
            TableName = seatedAt is null ? null : TableDisplayName(seatedAt),
            SeatNumber = seatedAt is null ? null : entry.SeatNumber
        };
    }

    private static string NormalizeSpaces(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string NameKey(string value) => NormalizeSpaces(value).ToUpperInvariant();

    /// <summary>Nome exibido no terminal/histórico: apelido, senão o nome completo.</summary>
    public static string PlayerDisplayName(Person? person) =>
        person is null
            ? "Jogador"
            : string.IsNullOrWhiteSpace(person.Nickname) ? person.FullName : person.Nickname;

    public static string TableDisplayName(TournamentTable table) =>
        string.IsNullOrWhiteSpace(table.TableName) ? $"Mesa {table.TableNumber}" : table.TableName;

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

        if (person.HomeGameId != tournament.HomeGameId)
            throw new DomainException("Jogador não pertence a este home game.");

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
        // Transação desde o início: o auto-assento lê a ocupação e precisa da trava de lugares
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await LockSeatingAsync(_db, tournamentId, ct);

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

        if (personMap.Values.Any(p => p.HomeGameId != tournament.HomeGameId))
            throw new DomainException("Jogador não pertence a este home game.");

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

        // Ocupação atual: jogadores ainda no torneio já assentados, por mesa.
        var seatedActive = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournament.Id
                     && (e.Status == nameof(EntryStatus.Active) || e.Status == nameof(EntryStatus.Registered))
                     && e.TableId != null)
            .ToListAsync(ct);

        var occupancy = tables.ToDictionary(
            t => t.Id,
            t => seatedActive.Where(e => e.TableId == t.Id).ToList());

        // Conta também as mesas quebradas (inativas): o número é único por torneio.
        var nextTableNumber = await _db.TournamentTables
            .Where(t => t.TournamentId == tournament.Id)
            .MaxAsync(t => t.TableNumber, ct) + 1;

        foreach (var entry in newEntries)
        {
            // Mesa com assento livre (lotação abaixo do MaxSeats E um número de lugar livre em
            // 1..MaxSeats) e menor lotação; empate → menor número.
            var target = tables
                .Where(t => occupancy[t.Id].Count < t.MaxSeats && NextSeat(t, occupancy[t.Id]).HasValue)
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

                // Passou a haver mais de uma mesa ativa: nenhuma é mais a mesa final
                foreach (var t in tables)
                    t.IsFinalTable = false;

                await _db.SaveChangesAsync(ct); // garante o Id real da nova mesa

                tables.Add(target);
                occupancy[target.Id] = [];

                RebalanceRandom(tables, occupancy);
                target = tables.OrderBy(t => occupancy[t.Id].Count).First();
            }

            // Assento = menor lugar livre da mesa; não mexe nos assentos de quem já está.
            // Sem lugar livre (dados antigos fora do MaxSeats) → fica sem mesa e o dealer o senta.
            var seat = NextSeat(target, occupancy[target.Id]);
            if (seat is null)
                continue;

            entry.TableId = target.Id;
            entry.SeatNumber = seat;
            occupancy[target.Id].Add(entry);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Menor lugar livre de 1..MaxSeats da mesa (<paramref name="seated"/> = quem ainda está
    /// no torneio sentado nela). Null quando não há lugar livre — nunca devolve além do MaxSeats.
    /// </summary>
    private static int? NextSeat(TournamentTable table, List<TournamentEntry> seated)
    {
        var taken = seated
            .Where(e => e.SeatNumber.HasValue)
            .Select(e => e.SeatNumber!.Value)
            .ToHashSet();
        for (var seat = 1; seat <= table.MaxSeats; seat++)
            if (!taken.Contains(seat))
                return seat;
        return null;
    }

    /// <summary>
    /// Move jogadores sorteados da mesa mais cheia para a mais vazia até a diferença ser ≤ 1.
    /// Quem é movido recebe o menor lugar livre no destino (sem colidir com quem já está lá).
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

            var seat = NextSeat(min, occupancy[min.Id]);
            if (seat is null)
                break; // mesa mais vazia sem lugar livre: não força ninguém além do MaxSeats

            var fromList = occupancy[max.Id];
            var idx = Random.Shared.Next(fromList.Count);
            var mover = fromList[idx];
            fromList.RemoveAt(idx);
            mover.TableId = min.Id;
            mover.SeatNumber = seat;
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

    /// <summary>
    /// Desfaz o rebuy MAIS RECENTE ainda valendo (botão do painel). A transação é marcada
    /// como desfeita (não é apagada) — mesmo fluxo do desfazer pelo histórico.
    /// </summary>
    public async Task<EntryResponse> RemoveRebuyAsync(
        Guid tournamentId, Guid entryId, Guid? removedBy = null, CancellationToken ct = default)
    {
        var lastRebuyTxId = await _db.Transactions
            .Where(t => t.TournamentId == tournamentId
                     && t.EntryId == entryId
                     && t.Type == nameof(TransactionType.Rebuy)
                     && t.ReversedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Jogador não tem rebuys para remover.");

        return await UndoRebuyAsync(tournamentId, entryId, lastRebuyTxId, removedBy, ct);
    }

    /// <summary>
    /// Desfaz UM rebuy específico: marca a transação (ReversedAt/ReversedBy/"Desfeito") e
    /// aplica os mesmos ajustes de contadores, saldo, bolo e custos automáticos do registro, ao contrário.
    /// </summary>
    public async Task<EntryResponse> UndoRebuyAsync(
        Guid tournamentId, Guid entryId, Guid transactionId, Guid? undoneBy = null, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        var rebuyTx = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == transactionId
                                   && t.TournamentId == tournamentId
                                   && t.EntryId == entryId
                                   && t.Type == nameof(TransactionType.Rebuy), ct)
            ?? throw new DomainException("Rebuy não encontrado.");

        if (rebuyTx.ReversedAt != null)
            throw new DomainException("Este item já foi desfeito.");

        if (entry.RebuyCount <= 0)
            throw new DomainException("Jogador não tem rebuys para remover.");

        rebuyTx.ReversedAt = DateTimeOffset.UtcNow;
        rebuyTx.ReversedBy = undoneBy;
        rebuyTx.ReversalReason = "Desfeito";

        entry.RebuyCount--;
        entry.RebuyTotal = entry.RebuyCount * (tournament.RebuyAmount ?? 0);
        RecalculateBalance(entry, tournament);

        tournament.TotalRebuys = Math.Max(0, tournament.TotalRebuys - 1);
        tournament.TotalPrizePool = Math.Max(0, tournament.TotalPrizePool - rebuyTx.Amount);
        await RecalcAutoCostsAndTotals(tournament, ct);

        await _db.SaveChangesAsync(ct);
        return _mapper.Map<EntryResponse>(entry);
    }

    /// <summary>Desfaz o add-on ainda valendo (botão do painel). Marca a transação como desfeita.</summary>
    public async Task<EntryResponse> RemoveAddonAsync(
        Guid tournamentId, Guid entryId, Guid? removedBy = null, CancellationToken ct = default)
    {
        var addonTxId = await _db.Transactions
            .Where(t => t.TournamentId == tournamentId
                     && t.EntryId == entryId
                     && t.Type == nameof(TransactionType.Addon)
                     && t.ReversedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Jogador não possui addon.");

        return await UndoAddonAsync(tournamentId, entryId, addonTxId, removedBy, ct);
    }

    /// <summary>
    /// Desfaz um add-on específico: marca a transação e aplica os mesmos ajustes do
    /// registro, ao contrário (contadores, saldo, bolo e custos automáticos).
    /// </summary>
    public async Task<EntryResponse> UndoAddonAsync(
        Guid tournamentId, Guid entryId, Guid transactionId, Guid? undoneBy = null, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        var addonTx = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == transactionId
                                   && t.TournamentId == tournamentId
                                   && t.EntryId == entryId
                                   && t.Type == nameof(TransactionType.Addon), ct)
            ?? throw new DomainException("Add-on não encontrado.");

        if (addonTx.ReversedAt != null)
            throw new DomainException("Este item já foi desfeito.");

        if (!entry.AddonPurchased)
            throw new DomainException("Jogador não possui addon.");

        addonTx.ReversedAt = DateTimeOffset.UtcNow;
        addonTx.ReversedBy = undoneBy;
        addonTx.ReversalReason = "Desfeito";

        entry.AddonPurchased = false;
        entry.AddonDouble = false;
        entry.AddonAmount = 0;
        RecalculateBalance(entry, tournament);

        tournament.TotalAddons = Math.Max(0, tournament.TotalAddons - 1);
        tournament.TotalPrizePool = Math.Max(0, tournament.TotalPrizePool - addonTx.Amount);
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

            // Quem eliminou tem que ser outro jogador deste torneio
            if (request.EliminatedByEntryId.HasValue
                && (request.EliminatedByEntryId == entryId
                    || !await _db.TournamentEntries.AnyAsync(e => e.Id == request.EliminatedByEntryId
                                                              && e.TournamentId == tournamentId, ct)))
                throw new DomainException("Quem eliminou não é um jogador deste torneio.");

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
        // Devolve o jogador a um lugar: serializa com move/quebra/sorteio antes de ler a ocupação
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await LockSeatingAsync(_db, tournamentId, ct);

        var tournament = await _db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct)
            ?? throw new DomainException("Torneio não encontrado.");

        var entry = await _db.TournamentEntries
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        // Prêmio já pago dele ou de quem mudaria de posição (campeão e quem saiu depois dele):
        // recalcular a classificação redistribuiria prêmios já entregues.
        var allEntries = await _db.TournamentEntries
            .Include(e => e.Person)
            .Where(e => e.TournamentId == tournamentId)
            .ToListAsync(ct);
        var paidPrizeEntryIds = await PaidPrizeEntryIdsAsync(tournamentId, ct);
        var paidBlocker = FindPaidPrizeBlockingUndoElimination(entry, allEntries, paidPrizeEntryIds);
        if (paidBlocker is not null)
            throw new DomainException(UndoEliminationPrizePaidMessage(paidBlocker));

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

        // Restaurar mesa/posicao se ainda estiver disponivel — só se a mesa ainda existe e está
        // ativa (mesa quebrada ou sorteio novo: o jogador volta sem mesa e o dealer o senta)
        var eliminationTableActive = entry.EliminationTableId.HasValue
            && await _db.TournamentTables.AnyAsync(t => t.Id == entry.EliminationTableId
                                                     && t.TournamentId == tournamentId
                                                     && t.IsActive, ct);
        if (eliminationTableActive && entry.EliminationSeatNumber.HasValue)
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
        await RecomputeStandingsAsync(tournament, paidPrizeEntryIds, ct);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return _mapper.Map<EntryResponse>(entry);
    }

    /// <summary>
    /// Recalcula posição final, ITM (premiado/eliminado) e vínculo de prêmio de TODOS os
    /// jogadores que já saíram, a partir da ordem de eliminação (último a sair = melhor
    /// posição entre os que saíram). Usado ao desfazer eliminação. Nunca reposiciona quem já
    /// recebeu o prêmio (PaidOut/PrizePaid/prêmio pago): posição, status e prêmio ficam como
    /// estão — o desfazer já recusa quando a posição de um pago mudaria.
    /// Define tournament.PlayersRemaining = nº de ativos.
    /// </summary>
    private async Task RecomputeStandingsAsync(
        Tournament tournament, ISet<Guid> paidPrizeEntryIds, CancellationToken ct)
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

        // Limpa vínculos de prêmio que não sejam pagos nem de jogadores já pagos (religados abaixo).
        var paidIds = entries
            .Where(e => IsPrizePaid(e, paidPrizeEntryIds))
            .Select(e => e.Id)
            .ToHashSet();
        foreach (var p in prizes)
            if (!p.Paid && (!p.EntryId.HasValue || !paidIds.Contains(p.EntryId.Value)))
                p.EntryId = null;

        for (int i = 0; i < finished.Count; i++)
        {
            var e = finished[i];
            var position = activeCount + 1 + i;

            if (paidIds.Contains(e.Id))
                continue; // já pago: mantém posição, status e prêmio

            e.FinalPosition = position;

            var isItm = position <= effectivePrizeCount;
            e.Status = isItm ? nameof(EntryStatus.Awarded) : nameof(EntryStatus.Eliminated);

            var prize = prizes.FirstOrDefault(p => p.Position == position);
            // Prêmio já pago a outra pessoa não troca de dono
            if (isItm && prize != null && (!prize.Paid || prize.EntryId == e.Id))
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
