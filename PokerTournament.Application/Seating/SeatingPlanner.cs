namespace PokerTournament.Application.Seating;

/// <summary>Mesa vista pelo planejador: ativa (com <see cref="Id"/>) ou a abrir (<see cref="Id"/> nulo).</summary>
public sealed class PlannedTable
{
    public Guid? Id { get; init; }
    public int Number { get; init; }
    public string Name { get; init; } = string.Empty;
    public int MaxSeats { get; init; }
    public bool IsNew => Id is null;
}

/// <summary>Jogador ainda no torneio (Active/Registered) sentado numa mesa ativa. Lugar nulo = dado antigo sem número.</summary>
public sealed record SeatedPlayer(Guid EntryId, int? SeatNumber);

/// <summary>Mesa ativa do torneio com quem está sentado nela.</summary>
public sealed class ActiveTableInput
{
    public Guid Id { get; init; }
    public int Number { get; init; }
    public string Name { get; init; } = string.Empty;
    public int MaxSeats { get; init; }
    public IReadOnlyList<SeatedPlayer> Occupants { get; init; } = [];
}

/// <summary>Lugar dado a um jogador sem mesa (inscrição nova ou, sem mesa ativa, ativo sem lugar).</summary>
public sealed record PlannedSeat(Guid EntryId, PlannedTable Table, int Seat);

/// <summary>Jogador já sentado que muda de mesa (sempre de uma mesa antiga para uma mesa aberta agora).</summary>
public sealed record PlannedMove(Guid EntryId, PlannedTable From, int? FromSeat, PlannedTable To, int ToSeat);

public sealed class SeatingPlan
{
    /// <summary>Mesas a abrir (números pela MA9), em ordem de número.</summary>
    public List<PlannedTable> Opened { get; } = [];
    public List<PlannedSeat> Seats { get; } = [];
    public List<PlannedMove> Moves { get; } = [];
    /// <summary>O passo 3 (estouro com equilíbrio) rodou.</summary>
    public bool Rebalanced { get; set; }
}

/// <summary>
/// Planejador de assentos da inscrição (SPEC-20261023-1214) — puro, sem EF, testável isolado. Regras:
/// <list type="bullet">
/// <item>MA2 — sem mesa ativa: abre ceil(N/S) mesas para os novos + os ativos sem lugar e distribui em rodízio
/// (embaralhado), com lugar sorteado em cada mesa. Sem movimentos.</item>
/// <item>MA3 — com vaga suficiente: cada novo vai para a mesa com menos jogadores que ainda tem vaga (empate → menor
/// número), em lugar livre sorteado. Vaga = min(números livres em 1..MaxSeats, MaxSeats − ocupantes).</item>
/// <item>MA4/MA5/MA6 — estourou: abre o menor m com vagas + m·S ≥ novos; alvos = (sentados + novos) / mesas, as
/// sobras (+1) primeiro para as mesas antigas mais cheias (empate → menor número), sem passar da lotação de cada
/// uma (o que não couber vai para a próxima). Das antigas acima do alvo saem sorteados; os novos preenchem primeiro
/// as antigas abaixo do alvo; o resto dos novos e todos os que saíram vão para as mesas novas, lugar sorteado.
/// Nunca de uma mesa antiga para outra antiga.</item>
/// <item>MA9 — número da mesa nova: o menor que nenhuma mesa ativa usa.</item>
/// </list>
/// Invariantes conferidos no fim (violação = bug: <see cref="InvalidOperationException"/>, quem chama aborta a
/// transação): nenhum lugar repetido nas mesas que receberam alguém, nenhuma dessas acima do MaxSeats, e — quando o
/// passo 3 rodou — diferença máx − mín ≤ 1 (mesa que ficou abaixo disso só se estiver na lotação dela).
/// </summary>
public static class SeatingPlanner
{
    public const int MinSeatsPerTable = 2;
    public const int MaxSeatsPerTable = 12;
    public const int DefaultSeatsPerTable = 9;

    /// <summary>Lotação de mesa nova: SeatsPerTable trazido para 2–12 (abaixo de 2 → 9; acima de 12 → 12).</summary>
    public static int EffectiveSeatsPerTable(int seatsPerTable) =>
        seatsPerTable < MinSeatsPerTable ? DefaultSeatsPerTable : Math.Min(seatsPerTable, MaxSeatsPerTable);

    public static string TableName(int number) => $"Mesa {number}";

    private sealed class TableState
    {
        public required PlannedTable Table { get; init; }
        public List<SeatedPlayer> Occupants { get; init; } = [];
        public List<int> Free { get; init; } = [];
        public int Count { get; set; }
        public int Target { get; set; }
        public int Cap { get; set; }
        // Lugares dados nesta passada (para conferir repetição)
        public List<int> Assigned { get; } = [];
        public bool Touched => Assigned.Count > 0;

        public int Vacancy => Math.Min(Free.Count, Math.Max(0, Table.MaxSeats - Count));
    }

    public static SeatingPlan Plan(
        IReadOnlyList<ActiveTableInput> activeTables,
        IReadOnlyList<Guid> newEntries,
        IReadOnlyList<Guid> unseatedActive,
        int seatsPerTable,
        Random rng)
    {
        var s = EffectiveSeatsPerTable(seatsPerTable);
        var plan = new SeatingPlan();
        var news = Shuffle(newEntries.Distinct(), rng);

        // ---------------------------------------------------------------- 1. sem mesa ativa (MA2)
        if (activeTables.Count == 0)
        {
            var all = Shuffle(news.Concat(unseatedActive.Where(id => !news.Contains(id))).Distinct(), rng);
            if (all.Count == 0)
                return plan;

            var count = (int)Math.Ceiling(all.Count / (double)s);
            var opened = NextFreeNumbers([], count)
                .Select(n => NewState(n, s))
                .ToList();
            for (var i = 0; i < all.Count; i++)
            {
                var st = opened[i % count];
                Place(st, rng);
                plan.Seats.Add(new PlannedSeat(all[i], st.Table, st.Assigned[^1]));
            }
            plan.Opened.AddRange(opened.Select(o => o.Table));
            Validate(opened, rebalanced: false);
            return plan;
        }

        var states = activeTables.Select(t =>
        {
            var taken = t.Occupants.Where(o => o.SeatNumber.HasValue).Select(o => o.SeatNumber!.Value).ToHashSet();
            return new TableState
            {
                Table = new PlannedTable { Id = t.Id, Number = t.Number, Name = t.Name, MaxSeats = t.MaxSeats },
                Occupants = [.. t.Occupants],
                Free = Enumerable.Range(1, Math.Max(0, t.MaxSeats)).Where(n => !taken.Contains(n)).ToList(),
                Count = t.Occupants.Count
            };
        }).ToList();

        if (news.Count == 0)
            return plan;

        // ---------------------------------------------------------------- 2. cabe nas mesas de hoje (MA3)
        var vacancies = states.Sum(st => st.Vacancy);
        if (vacancies >= news.Count)
        {
            foreach (var entryId in news)
            {
                var target = states
                    .Where(st => st.Vacancy > 0)
                    .OrderBy(st => st.Count)
                    .ThenBy(st => st.Table.Number)
                    .First();
                Place(target, rng);
                plan.Seats.Add(new PlannedSeat(entryId, target.Table, target.Assigned[^1]));
            }
            Validate(states, rebalanced: false);
            return plan;
        }

        // ---------------------------------------------------------------- 3. estourou: abre e equilibra (MA4–MA6)
        var m = (int)Math.Ceiling((news.Count - vacancies) / (double)s);
        var newStates = NextFreeNumbers(states.Select(st => st.Table.Number), m)
            .Select(n => NewState(n, s))
            .ToList();

        foreach (var st in states)
            st.Cap = st.Count + st.Vacancy; // lotação real: quem está + a vaga que sobra
        foreach (var st in newStates)
            st.Cap = s;

        var total = states.Sum(st => st.Count) + news.Count;
        var tableCount = states.Count + newStates.Count;
        // Ordem das sobras: antigas mais cheias primeiro (empate → menor número), depois as novas por número
        var ordered = states
            .OrderByDescending(st => st.Count)
            .ThenBy(st => st.Table.Number)
            .Concat(newStates)
            .ToList();

        var baseTarget = total / tableCount;
        foreach (var st in ordered)
            st.Target = Math.Min(baseTarget, st.Cap);
        var left = total - ordered.Sum(st => st.Target);
        while (left > 0)
        {
            var progressed = false;
            foreach (var st in ordered)
            {
                if (left == 0) break;
                if (st.Target >= st.Cap) continue;
                st.Target++;
                left--;
                progressed = true;
            }
            if (!progressed)
                throw new InvalidOperationException("Planejador de assentos: lotação insuficiente para os alvos.");
        }

        // Saem sorteados das mesas antigas acima do alvo
        var movers = new List<(Guid EntryId, TableState From, int? FromSeat)>();
        foreach (var st in states.OrderBy(st => st.Table.Number))
        {
            var excess = st.Count - st.Target;
            for (var i = 0; i < excess; i++)
            {
                var idx = rng.Next(st.Occupants.Count);
                var leaving = st.Occupants[idx];
                st.Occupants.RemoveAt(idx);
                st.Count--;
                if (leaving.SeatNumber is { } seat && seat >= 1 && seat <= st.Table.MaxSeats)
                    st.Free.Add(seat);
                movers.Add((leaving.EntryId, st, leaving.SeatNumber));
            }
        }

        // Novos preenchem primeiro as antigas abaixo do alvo
        var queue = new Queue<Guid>(news);
        foreach (var st in states.OrderBy(st => st.Table.Number))
        {
            while (st.Count < st.Target)
            {
                if (queue.Count == 0)
                    throw new InvalidOperationException("Planejador de assentos: faltaram jogadores novos para as mesas antigas.");
                Place(st, rng);
                plan.Seats.Add(new PlannedSeat(queue.Dequeue(), st.Table, st.Assigned[^1]));
            }
        }

        // O resto dos novos e quem saiu vão para as mesas abertas agora
        var pool = Shuffle(
            queue.Select(id => (EntryId: id, From: (TableState?)null, FromSeat: (int?)null))
                .Concat(movers.Select(mv => (mv.EntryId, From: (TableState?)mv.From, mv.FromSeat))),
            rng);
        var next = 0;
        foreach (var st in newStates)
        {
            while (st.Count < st.Target)
            {
                if (next >= pool.Count)
                    throw new InvalidOperationException("Planejador de assentos: faltaram jogadores para as mesas novas.");
                var item = pool[next++];
                Place(st, rng);
                var seat = st.Assigned[^1];
                if (item.From is null)
                    plan.Seats.Add(new PlannedSeat(item.EntryId, st.Table, seat));
                else
                    plan.Moves.Add(new PlannedMove(item.EntryId, item.From.Table, item.FromSeat, st.Table, seat));
            }
        }
        if (next != pool.Count)
            throw new InvalidOperationException("Planejador de assentos: sobrou jogador sem lugar.");

        plan.Opened.AddRange(newStates.Select(st => st.Table));
        plan.Rebalanced = true;
        Validate(states.Concat(newStates).ToList(), rebalanced: true);
        return plan;
    }

    /// <summary>Os <paramref name="count"/> menores números ≥ 1 que nenhuma mesa ativa usa.</summary>
    public static List<int> NextFreeNumbers(IEnumerable<int> used, int count)
    {
        var taken = used.ToHashSet();
        var result = new List<int>(count);
        for (var n = 1; result.Count < count; n++)
            if (!taken.Contains(n))
                result.Add(n);
        return result;
    }

    private static TableState NewState(int number, int seats) => new()
    {
        Table = new PlannedTable { Id = null, Number = number, Name = TableName(number), MaxSeats = seats },
        Free = Enumerable.Range(1, seats).ToList(),
        Count = 0
    };

    /// <summary>Senta alguém num lugar livre sorteado da mesa (o lugar fica em <c>Assigned[^1]</c>).</summary>
    private static void Place(TableState st, Random rng)
    {
        if (st.Free.Count == 0 || st.Count >= st.Table.MaxSeats)
            throw new InvalidOperationException($"Planejador de assentos: {st.Table.Name} sem lugar livre.");
        var idx = rng.Next(st.Free.Count);
        var seat = st.Free[idx];
        st.Free.RemoveAt(idx);
        st.Count++;
        st.Assigned.Add(seat);
    }

    private static void Validate(IReadOnlyList<TableState> tables, bool rebalanced)
    {
        foreach (var st in tables.Where(t => t.Touched))
        {
            if (st.Count > st.Table.MaxSeats)
                throw new InvalidOperationException($"Planejador de assentos: {st.Table.Name} acima da lotação.");
            var remaining = st.Occupants.Where(o => o.SeatNumber.HasValue).Select(o => o.SeatNumber!.Value);
            var seats = st.Assigned.Concat(remaining).ToList();
            if (seats.Count != seats.Distinct().Count())
                throw new InvalidOperationException($"Planejador de assentos: lugar repetido na {st.Table.Name}.");
            if (st.Assigned.Any(n => n < 1 || n > st.Table.MaxSeats))
                throw new InvalidOperationException($"Planejador de assentos: lugar fora de 1..{st.Table.MaxSeats} na {st.Table.Name}.");
        }

        if (rebalanced)
        {
            var max = tables.Max(t => t.Count);
            // Abaixo de máx − 1 só a mesa que já está na lotação dela (mesa antiga menor que as outras)
            if (tables.Any(t => t.Count < max - 1 && t.Count < t.Cap))
                throw new InvalidOperationException("Planejador de assentos: mesas desequilibradas depois de abrir mesa.");
        }
    }

    private static List<T> Shuffle<T>(IEnumerable<T> source, Random rng)
    {
        var list = source.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
