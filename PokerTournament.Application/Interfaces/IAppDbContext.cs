using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Interfaces;

public interface IAppDbContext
{
    DbSet<Person> Persons { get; }
    DbSet<User> Users { get; }
    DbSet<HomeGame> HomeGames { get; }
    DbSet<HomeGameMember> HomeGameMembers { get; }
    DbSet<Ranking> Rankings { get; }
    DbSet<ScoringRule> ScoringRules { get; }
    DbSet<BlindStructure> BlindStructures { get; }
    DbSet<BlindLevel> BlindLevels { get; }
    DbSet<TournamentBlindLevel> TournamentBlindLevels { get; }
    DbSet<Tournament> Tournaments { get; }
    DbSet<TournamentEntry> TournamentEntries { get; }
    DbSet<TournamentTable> TournamentTables { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<CostExtra> CostExtras { get; }
    DbSet<TournamentPrize> TournamentPrizes { get; }
    DbSet<Elimination> Eliminations { get; }
    DbSet<RankingScore> RankingScores { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<NotificationLog> NotificationLogs { get; }
    DbSet<TournamentDealer> TournamentDealers { get; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
