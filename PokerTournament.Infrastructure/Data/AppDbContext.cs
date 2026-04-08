using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Person> Persons => Set<Person>();
    public DbSet<User> Users => Set<User>();
    public DbSet<HomeGame> HomeGames => Set<HomeGame>();
    public DbSet<HomeGameMember> HomeGameMembers => Set<HomeGameMember>();
    public DbSet<Ranking> Rankings => Set<Ranking>();
    public DbSet<ScoringRule> ScoringRules => Set<ScoringRule>();
    public DbSet<BlindStructure> BlindStructures => Set<BlindStructure>();
    public DbSet<BlindLevel> BlindLevels => Set<BlindLevel>();
    public DbSet<TournamentBlindLevel> TournamentBlindLevels => Set<TournamentBlindLevel>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentEntry> TournamentEntries => Set<TournamentEntry>();
    public DbSet<TournamentTable> TournamentTables => Set<TournamentTable>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<CostExtra> CostExtras => Set<CostExtra>();
    public DbSet<TournamentPrize> TournamentPrizes => Set<TournamentPrize>();
    public DbSet<Elimination> Eliminations => Set<Elimination>();
    public DbSet<RankingScore> RankingScores => Set<RankingScore>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<TournamentDealer> TournamentDealers => Set<TournamentDealer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var schema = Environment.GetEnvironmentVariable("DB_SCHEMA") ?? "public";
        modelBuilder.HasDefaultSchema(schema);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global query filter for soft-deletable entities
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(SoftDeletableEntity).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ApplySoftDeleteFilter),
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .MakeGenericMethod(entityType.ClrType);

                method.Invoke(null, [modelBuilder]);
            }
        }
    }

    private static void ApplySoftDeleteFilter<T>(ModelBuilder modelBuilder) where T : SoftDeletableEntity
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.DeletedAt == null);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.Id == Guid.Empty)
                        entry.Entity.Id = Guid.NewGuid();
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }

        foreach (var entry in ChangeTracker.Entries<SoftDeletableEntity>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.DeletedAt = now;
                entry.Entity.UpdatedAt = now;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
