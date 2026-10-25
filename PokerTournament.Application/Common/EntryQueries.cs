using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Common;

/// <summary>
/// Inscrições com a pessoa carregada MESMO quando ela foi removida do cadastro do home game (revisão geral do
/// programa, ondas 7–9). <see cref="Person"/> é soft delete com filtro global (DeletedAt == null) e é o lado
/// obrigatório da relação com <see cref="TournamentEntry"/>: qualquer <c>Include(e =&gt; e.Person)</c> ou
/// <c>e.Person.X</c> numa projeção vira INNER JOIN com o filtro e a inscrição SOME da consulta. Aí o lugar dela parece
/// livre (lugar repetido, mesa acima do MaxSeats), eliminar/pagar/excluir responde "Inscrição não encontrada." e o
/// telão, o terminal e o histórico contam errado. Regra: jogador removido do cadastro continua no torneio com o nome.
/// <para>
/// <c>IgnoreQueryFilters</c> vale para a consulta INTEIRA. Nas consultas de inscrição, mesa, prêmio e transação isso só
/// solta a pessoa (TournamentEntry, TournamentTable, TournamentPrize, Transaction e Elimination não têm filtro), desde
/// que a consulta não navegue por Tournament/HomeGame/Ranking — o torneio é validado à parte, como os serviços já
/// fazem. Use estes atalhos (ou <c>IgnoreQueryFilters()</c> com o mesmo cuidado) em toda consulta de ocupação e de
/// fluxo do torneio que dependa da pessoa.
/// </para>
/// </summary>
public static class EntryQueries
{
    /// <summary>Inscrições (rastreadas) com a pessoa, inclusive a removida do cadastro.</summary>
    public static IQueryable<TournamentEntry> EntriesWithPerson(this IAppDbContext db) =>
        db.TournamentEntries.IgnoreQueryFilters().Include(e => e.Person);
}
