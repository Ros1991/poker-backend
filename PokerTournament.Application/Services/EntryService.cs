using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Application.Seating;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

public class EntryService
{
    private readonly IAppDbContext _db;
    private readonly IMapper _mapper;
    private readonly IWhatsAppProvider _whatsApp;
    private readonly PlanService _plans;

    public EntryService(IAppDbContext db, IMapper mapper, IWhatsAppProvider whatsApp, PlanService plans)
    {
        _db = db;
        _mapper = mapper;
        _whatsApp = whatsApp;
        _plans = plans;
    }

    // Inscrição sem "buy-in pago" (SPEC-20261023-1213, F15): toda inscrição nasce Pendente (TotalPaid 0, Balance =
    // custo da entrada) e o dinheiro entra só pela tela de pagamentos (forma de pagamento e caixa corretos). Isso
    // também cumpre a D3 da SPEC-20261023-1205 (no Mesa nem marcar como paga) sem checar plano: inscrição nunca é
    // recusada por plano. TournamentEntry.BuyInPaid fica como legado (sempre false em inscrição nova).

    public async Task<List<EntryResponse>> GetEntriesAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var entries = await _db.TournamentEntries
            .Include(e => e.Person)
            .Where(e => e.TournamentId == tournamentId)
            .OrderBy(e => e.RegisteredAt)
            .ToListAsync(ct);

        return _mapper.Map<List<EntryResponse>>(entries);
    }

    /// <summary>
    /// "Excluir inscrição" do painel (só organizador). Deixa rastro no histórico (SPEC-20261023-1217, L5): atividade
    /// PlayerRemoved "Inscrição excluída: Fulano (buy-in, 2 rebuys e add-on · R$ 350,00)" com quem excluiu, gravada
    /// junto com a exclusão. O resumo é montado antes de apagar, porque as transações do jogador somem com ela.
    /// </summary>
    public async Task DeleteEntryAsync(
        Guid tournamentId, Guid entryId, Guid? deletedBy = null, CancellationToken ct = default)
    {
        // Tira alguém da ocupação das mesas: mesma trava da inscrição (SPEC-20261023-1214, MA14)
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await LockSeatingAsync(_db, tournamentId, ct);

        var entry = await _db.TournamentEntries
            .Include(e => e.Transactions)
            .Include(e => e.Person)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TournamentId == tournamentId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        var description = TournamentHistoryService.DeletedEntryDescription(entry);

        await RemoveEntryAndAdjustTotalsAsync(tournamentId, entry, ct);

        _db.TournamentActivities.Add(new TournamentActivity
        {
            TournamentId = tournamentId,
            EntryId = entryId, // sem FK: a inscrição some, o registro fica
            Type = nameof(TournamentActivityType.PlayerRemoved),
            Description = description.Length > 1000 ? description[..997] + "..." : description,
            CreatedBy = deletedBy
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
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
    /// Serializa, por torneio, as operações que mexem em lugares ou na ocupação das mesas (mover, quebrar,
    /// sortear, criar mesa, inscrição individual e em lote, eliminar, desfazer eliminação, desfazer/excluir
    /// inscrição, tirar da mesa — SPEC-20261023-1214, MA14): sem isso, duas requisições leem o mesmo lugar
    /// livre e as duas sentam alguém nele (linhas diferentes, o xmin não pega), ou uma eliminação simultânea
    /// faz a mesa parecer cheia e a inscrição abre mesa à toa. pg_advisory_xact_lock com chave tirada do id do
    /// torneio; tem que rodar DENTRO da transação explícita (solta no commit/rollback) e quem chama lê a
    /// ocupação DEPOIS.
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
        // Libera um lugar: mesma trava da inscrição (MA14). Não desfaz mesa aberta nem devolve quem mudou (MA11)
        await LockSeatingAsync(_db, tournamentId, ct);

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

    /// <summary>Nome exibido no terminal/histórico: apelido, senão o nome completo.</summary>
    public static string PlayerDisplayName(Person? person) =>
        person is null
            ? "Jogador"
            : string.IsNullOrWhiteSpace(person.Nickname) ? person.FullName : person.Nickname;

    public static string TableDisplayName(TournamentTable table) =>
        string.IsNullOrWhiteSpace(table.TableName) ? $"Mesa {table.TableNumber}" : table.TableName;

    /// <summary>
    /// Inscrição individual (POST entries): já senta o jogador pelo planejador (SPEC-20261023-1214, MA1) dentro da
    /// transação com a trava de lugares. Abre a transação só se ainda não houver uma (BeginTransaction aninhado dá
    /// erro no EF); a trava vem antes de qualquer leitura de ocupação.
    /// </summary>
    public async Task<EntryResponse> RegisterEntryAsync(
        Guid tournamentId, CreateEntryRequest request, Guid? registeredBy = null, CancellationToken ct = default)
    {
        var ownTx = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            await LockSeatingAsync(_db, tournamentId, ct);

            var (entry, tournament) = await RegisterEntryCoreAsync(tournamentId, request, registeredBy, ct);
            await SeatNewEntriesAsync(tournament, [entry], registeredBy, ct);

            if (ownTx is not null)
                await ownTx.CommitAsync(ct);
            return _mapper.Map<EntryResponse>(entry);
        }
        finally
        {
            if (ownTx is not null)
                await ownTx.DisposeAsync();
        }
    }

    /// <summary>
    /// Núcleo da inscrição individual: regras, contadores, custos automáticos e transação de buy-in; salva. Roda
    /// dentro da transação de quem chama (que já pegou a trava). Não senta ninguém.
    /// </summary>
    private async Task<(TournamentEntry Entry, Tournament Tournament)> RegisterEntryCoreAsync(
        Guid tournamentId, CreateEntryRequest request, Guid? registeredBy, CancellationToken ct)
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

        // Sempre Pendente (F15): o pagamento é registrado depois, pela tela de pagamentos
        var entry = new TournamentEntry
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            PersonId = request.PersonId,
            Status = nameof(EntryStatus.Active),
            BuyInPaid = false,
            BuyInAmount = tournament.BuyInAmount,
            RebuyCount = 0,
            RebuyTotal = 0,
            AddonPurchased = false,
            AddonAmount = 0,
            TotalDue = entryCost,
            TotalPaid = 0,
            Balance = entryCost,
            PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending),
            EntryNumber = entryNumber,
            IsReentry = entryNumber > 1,
            RegisteredAt = DateTimeOffset.UtcNow,
            RegisteredBy = registeredBy
        };

        _db.TournamentEntries.Add(entry);
        // Nunca volta a false: o rascunho que já teve inscrição não devolve a vaga do limite semanal ao ser excluído
        tournament.HadEntries = true;

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
        return (entry, tournament);
    }

    /// <summary>
    /// Inscreve vários jogadores de uma vez (multi-seleção) — rota única do painel e do terminal (F13). Atômico
    /// (transação com a trava de lugares). Todas nascem Pendentes. Depois de criar as inscrições, senta todo mundo
    /// numa passada só pelo planejador (SPEC-20261023-1214, MA6): a primeira inscrição cria a Mesa 1, as seguintes
    /// vão para a mesa com menos gente e, quando tudo enche, abre mesa e equilibra. Devolve as inscrições (já com
    /// mesa/lugar) e o resultado do assento (MA16).
    /// </summary>
    public async Task<BulkCreateEntriesResponse> RegisterEntriesBulkAsync(
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
        // Nunca volta a false (limite semanal do Mesa: rascunho que já teve inscrição não devolve a vaga)
        tournament.HadEntries = true;

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
                BuyInPaid = false,
                BuyInAmount = tournament.BuyInAmount,
                RebuyCount = 0,
                RebuyTotal = 0,
                AddonPurchased = false,
                AddonAmount = 0,
                TotalDue = entryCost,
                TotalPaid = 0,
                Balance = entryCost,
                PaymentStatus = nameof(Domain.Enums.PaymentStatus.Pending),
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

        // Senta todos numa passada só (MA6); com o plano do dono sem Tables, ninguém senta (MA13)
        var seating = await SeatNewEntriesAsync(tournament, createdEntries, registeredBy, ct);

        await tx.CommitAsync(ct);

        return new BulkCreateEntriesResponse
        {
            Entries = createdEntries.Select(e => _mapper.Map<EntryResponse>(e)).ToList(),
            Seating = seating
        };
    }

    /// <summary>Lotação das mesas novas do torneio: SeatsPerTable trazido para 2–12 (abaixo de 2 → 9).</summary>
    public static int EffectiveSeatsPerTable(Tournament tournament) =>
        SeatingPlanner.EffectiveSeatsPerTable(tournament.SeatsPerTable);

    /// <summary>
    /// Senta as inscrições novas (SPEC-20261023-1214) — ÚNICO lugar em que a inscrição senta jogador. Roda dentro da
    /// transação de quem chama, DEPOIS da trava de lugares e do SaveChanges das inscrições. Só com o recurso Tables
    /// no plano efetivo do DONO (MA13; consulta silenciosa, nunca recusa — Plans:Enforce=false libera): sem ele,
    /// ninguém senta e nenhuma mesa é criada. Lê a ocupação, chama o <see cref="SeatingPlanner"/>, abre/reaproveita
    /// mesas (<see cref="TableOpener"/>), aplica lugares e movimentos e grava o histórico da MA15:
    /// TableOpened ("Mesa 2 aberta: 4 jogadores movidos — …"), PlayerMoved só no histórico de quem mudou e
    /// PlayerSeated ("Fulano sentou na Mesa X, lugar Y" visível com 1 inscrito; com 2 ou mais, uma linha resumo
    /// visível e a de cada um só no histórico dele). Autor = <paramref name="registeredBy"/>. Salva.
    /// Ativos sem lugar só entram na conta quando não há mesa ativa (MA2/MA17).
    /// </summary>
    private async Task<SeatingResultResponse> SeatNewEntriesAsync(
        Tournament tournament, List<TournamentEntry> newEntries, Guid? registeredBy, CancellationToken ct)
    {
        var result = new SeatingResultResponse
        {
            InProgress = tournament.Status == nameof(TournamentStatus.InProgress)
        };

        result.TablesEnabled = await _plans.HasFeatureForTournamentAsync(tournament.Id, PlanFeature.Tables, ct);
        if (!result.TablesEnabled || newEntries.Count == 0)
            return result;

        var tables = await _db.TournamentTables
            .Where(t => t.TournamentId == tournament.Id && t.IsActive)
            .OrderBy(t => t.TableNumber)
            .ToListAsync(ct);
        var tableIds = tables.Select(t => t.Id).ToHashSet();

        var newIds = newEntries.Select(e => e.Id).ToHashSet();
        var playing = await _db.TournamentEntries
            .Include(e => e.Person)
            .Where(e => e.TournamentId == tournament.Id
                     && (e.Status == nameof(EntryStatus.Active) || e.Status == nameof(EntryStatus.Registered)))
            .ToListAsync(ct);
        var entryById = playing.ToDictionary(e => e.Id);
        foreach (var e in newEntries)
            entryById[e.Id] = e;

        var seated = playing
            .Where(e => !newIds.Contains(e.Id) && e.TableId is { } tid && tableIds.Contains(tid))
            .ToList();
        // MA2: sem mesa ativa, quem já está inscrito sem lugar senta junto; com mesa, fica para o dealer (MA17)
        var unseated = tables.Count == 0
            ? playing.Where(e => !newIds.Contains(e.Id)).Select(e => e.Id).ToList()
            : [];

        var inputs = tables.Select(t => new ActiveTableInput
        {
            Id = t.Id,
            Number = t.TableNumber,
            Name = TableDisplayName(t),
            MaxSeats = t.MaxSeats,
            Occupants = seated.Where(e => e.TableId == t.Id).Select(e => new SeatedPlayer(e.Id, e.SeatNumber)).ToList()
        }).ToList();

        var plan = SeatingPlanner.Plan(
            inputs, newEntries.Select(e => e.Id).ToList(), unseated, EffectiveSeatsPerTable(tournament), Random.Shared);

        // Abre/reaproveita as mesas ANTES de aplicar os lugares (reativar limpa quem ainda apontava para a linha)
        var realTable = tables.ToDictionary(t => t.Id);
        var opened = new Dictionary<PlannedTable, TournamentTable>();
        foreach (var p in plan.Opened)
        {
            var table = await TableOpener.OpenAsync(_db, tournament.Id, p.Number, p.MaxSeats, p.Name, ct);
            opened[p] = table;
        }
        TournamentTable Resolve(PlannedTable p) => p.Id is { } id ? realTable[id] : opened[p];

        foreach (var s in plan.Seats)
        {
            var e = entryById[s.EntryId];
            e.TableId = Resolve(s.Table).Id;
            e.SeatNumber = s.Seat;
        }
        foreach (var mv in plan.Moves)
        {
            var e = entryById[mv.EntryId];
            e.TableId = Resolve(mv.To).Id;
            e.SeatNumber = mv.ToSeat;
        }

        // ---------------------------------------------------------------- resposta
        result.OpenedTables = plan.Opened
            .Select(p => new SeatingOpenedTable { TableId = opened[p].Id, TableName = TableDisplayName(opened[p]) })
            .ToList();
        result.Seated = plan.Seats
            .Select(s => new SeatingSeat
            {
                EntryId = s.EntryId,
                PlayerName = PlayerDisplayName(entryById[s.EntryId].Person),
                TableId = Resolve(s.Table).Id,
                TableName = TableDisplayName(Resolve(s.Table)),
                SeatNumber = s.Seat
            })
            // Os inscritos agora primeiro (na ordem do pedido), depois quem estava sem lugar
            .OrderBy(s => newIds.Contains(s.EntryId) ? newEntries.FindIndex(e => e.Id == s.EntryId) : int.MaxValue)
            .ToList();
        result.Moves = plan.Moves
            .OrderBy(mv => mv.To.Number)
            .ThenBy(mv => mv.ToSeat)
            .Select(mv => new SeatingMove
            {
                EntryId = mv.EntryId,
                PlayerName = PlayerDisplayName(entryById[mv.EntryId].Person),
                FromTableId = Resolve(mv.From).Id,
                FromTableName = TableDisplayName(Resolve(mv.From)),
                FromSeat = mv.FromSeat,
                ToTableId = Resolve(mv.To).Id,
                ToTableName = TableDisplayName(Resolve(mv.To)),
                ToSeat = mv.ToSeat
            })
            .ToList();

        var openedLabel = OpenedTablesLabel(result.OpenedTables.Select(t => t.TableName).ToList());
        var newSeated = result.Seated.Where(s => newIds.Contains(s.EntryId)).ToList();
        var oldUnseatedSeated = result.Seated.Count - newSeated.Count;
        result.Summary = result.Moves.Count > 0
            ? $"{openedLabel}: {Plural(result.Moves.Count, "jogador movido", "jogadores movidos")}"
            : openedLabel ?? SeatDistribution(newSeated);

        // ---------------------------------------------------------------- histórico (MA15)
        if (openedLabel is not null)
        {
            var description = openedLabel;
            if (result.Moves.Count > 0)
                description += $": {Plural(result.Moves.Count, "jogador movido", "jogadores movidos")} — "
                             + string.Join("; ", result.Moves.Select(MoveText));
            else if (oldUnseatedSeated > 0)
                description += $": {Plural(oldUnseatedSeated, "jogador que estava sem mesa também sentou", "jogadores que estavam sem mesa também sentaram")}";
            _db.TournamentActivities.Add(new TournamentActivity
            {
                TournamentId = tournament.Id,
                Type = nameof(TournamentActivityType.TableOpened),
                Description = Cut(description),
                CreatedBy = registeredBy
            });
        }

        foreach (var mv in result.Moves)
        {
            _db.TournamentActivities.Add(new TournamentActivity
            {
                TournamentId = tournament.Id,
                EntryId = mv.EntryId,
                Type = nameof(TournamentActivityType.PlayerMoved),
                Description = Cut($"{mv.ToTableName} aberta — {MoveText(mv)}"),
                CreatedBy = registeredBy,
                PlayerOnly = true
            });
        }

        if (newSeated.Count >= 2)
        {
            _db.TournamentActivities.Add(new TournamentActivity
            {
                TournamentId = tournament.Id,
                Type = nameof(TournamentActivityType.PlayerSeated),
                Description = Cut($"{newSeated.Count} jogadores inscritos e sentados: {SeatCounts(newSeated)}"),
                CreatedBy = registeredBy
            });
        }
        foreach (var s in result.Seated)
        {
            _db.TournamentActivities.Add(new TournamentActivity
            {
                TournamentId = tournament.Id,
                EntryId = s.EntryId,
                Type = nameof(TournamentActivityType.PlayerSeated),
                Description = $"{s.PlayerName} sentou na {s.TableName}, lugar {s.SeatNumber}",
                CreatedBy = registeredBy,
                // Visível só quando entrou um jogador só; com lote, o resumo acima é a linha visível
                PlayerOnly = newSeated.Count >= 2 || !newIds.Contains(s.EntryId)
            });
        }

        await _db.SaveChangesAsync(ct);
        return result;
    }

    private static string MoveText(SeatingMove mv) =>
        $"{mv.PlayerName}: {mv.FromTableName}{(mv.FromSeat is { } fs ? $", lugar {fs}" : string.Empty)} → {mv.ToTableName}, lugar {mv.ToSeat}";

    /// <summary>"Mesa 2 aberta" / "Mesas 2 e 3 abertas" / "Mesas 1, 2 e 3 abertas"; nulo sem mesa aberta.</summary>
    private static string? OpenedTablesLabel(List<string> names) => names.Count switch
    {
        0 => null,
        1 => $"{names[0]} aberta",
        _ => $"Mesas {JoinNumbers(names)} abertas"
    };

    // "Mesa 1", "Mesa 2" → "1 e 2"; nomes fora do padrão entram inteiros
    private static string JoinNumbers(List<string> names)
    {
        var parts = names.Select(n => n.StartsWith("Mesa ", StringComparison.Ordinal) ? n[5..] : n).ToList();
        return parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " e " + parts[^1];
    }

    /// <summary>"Mesa 1 (6), Mesa 2 (6)" na ordem das mesas.</summary>
    private static string SeatCounts(IEnumerable<SeatingSeat> seats) =>
        string.Join(", ", seats
            .GroupBy(s => s.TableName)
            .OrderBy(g => g.Key.Length).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key} ({g.Count()})"));

    /// <summary>Resumo sem mesa aberta: "Fulano sentou na Mesa 1, lugar 3" ou "3 jogadores sentados: Mesa 1 (2), Mesa 2 (1)".</summary>
    private static string? SeatDistribution(List<SeatingSeat> seats) => seats.Count switch
    {
        0 => null,
        1 => $"{seats[0].PlayerName} sentou na {seats[0].TableName}, lugar {seats[0].SeatNumber}",
        _ => $"{seats.Count} jogadores sentados: {SeatCounts(seats)}"
    };

    private static string Plural(int n, string singular, string plural) =>
        $"{n} {(n == 1 ? singular : plural)}";

    // tournament_activities.description: até 1000 caracteres
    private static string Cut(string text) => text.Length > 1000 ? text[..997] + "..." : text;

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
            // Tira o eliminado da mesa: mesma trava da inscrição (MA14) — sem ela, uma inscrição simultânea veria a
            // mesa cheia e abriria mesa à toa. Antes de qualquer leitura de ocupação.
            await LockSeatingAsync(_db, tournamentId, ct);

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
