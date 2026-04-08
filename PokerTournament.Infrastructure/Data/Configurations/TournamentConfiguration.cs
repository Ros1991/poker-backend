using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class TournamentConfiguration : IEntityTypeConfiguration<Tournament>
{
    public void Configure(EntityTypeBuilder<Tournament> builder)
    {
        builder.ToTable("tournaments", t =>
        {
            t.HasCheckConstraint("ck_tournaments_status",
                "status IN ('Draft','OpenForRegistration','InProgress','BreakSettlement','Finished','Cancelled')");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.DeletedAt).HasColumnName("deleted_at");
        builder.Property(t => t.xmin)
            .IsRowVersion();

        builder.Property(t => t.HomeGameId).HasColumnName("home_game_id");
        builder.Property(t => t.RankingId).HasColumnName("ranking_id");
        builder.Property(t => t.BlindStructureId).HasColumnName("blind_structure_id");

        builder.Property(t => t.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Description).HasColumnName("description");
        builder.Property(t => t.Date).HasColumnName("date");
        builder.Property(t => t.StartTime).HasColumnName("start_time");

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(50);

        // Money fields
        builder.Property(t => t.BuyInAmount).HasColumnName("buy_in_amount").HasColumnType("decimal(10,2)");
        builder.Property(t => t.RebuyAmount).HasColumnName("rebuy_amount").HasColumnType("decimal(10,2)");
        builder.Property(t => t.AddonAmount).HasColumnName("addon_amount").HasColumnType("decimal(10,2)");

        // Chip stacks
        builder.Property(t => t.StartingStack).HasColumnName("starting_stack");
        builder.Property(t => t.RebuyStack).HasColumnName("rebuy_stack");
        builder.Property(t => t.AddonStack).HasColumnName("addon_stack");

        // Rebuy / Addon rules
        builder.Property(t => t.MaxRebuys).HasColumnName("max_rebuys");
        builder.Property(t => t.AddonAllowed).HasColumnName("addon_allowed");
        builder.Property(t => t.AddonDoubleAllowed).HasColumnName("addon_double_allowed").HasDefaultValue(false);
        builder.Property(t => t.LateRegistrationLevel).HasColumnName("late_registration_level");
        builder.Property(t => t.RebuyUntilLevel).HasColumnName("rebuy_until_level");
        builder.Property(t => t.SeatsPerTable).HasColumnName("seats_per_table").HasDefaultValue(9);

        // Timer state
        builder.Property(t => t.CurrentLevel).HasColumnName("current_level");
        builder.Property(t => t.TimerStartedAt).HasColumnName("timer_started_at");
        builder.Property(t => t.TimerPausedAt).HasColumnName("timer_paused_at");
        builder.Property(t => t.TimerElapsedSeconds).HasColumnName("timer_elapsed_seconds");
        builder.Property(t => t.IsTimerRunning).HasColumnName("is_timer_running");

        // Prize pool
        builder.Property(t => t.TotalPrizePool).HasColumnName("total_prize_pool").HasColumnType("decimal(10,2)");
        builder.Property(t => t.TotalCosts).HasColumnName("total_costs").HasColumnType("decimal(10,2)");
        builder.Property(t => t.NetPrizePool).HasColumnName("net_prize_pool").HasColumnType("decimal(10,2)");
        builder.Property(t => t.PrizeConfirmed).HasColumnName("prize_confirmed");
        builder.Property(t => t.PrizeConfirmedAt).HasColumnName("prize_confirmed_at");

        // Settlement
        builder.Property(t => t.SettlementClosed).HasColumnName("settlement_closed");
        builder.Property(t => t.SettlementClosedAt).HasColumnName("settlement_closed_at");
        builder.Property(t => t.SettlementClosedBy).HasColumnName("settlement_closed_by");

        // Counters
        builder.Property(t => t.TotalEntries).HasColumnName("total_entries");
        builder.Property(t => t.TotalRebuys).HasColumnName("total_rebuys");
        builder.Property(t => t.TotalAddons).HasColumnName("total_addons");
        builder.Property(t => t.PlayersRemaining).HasColumnName("players_remaining");

        builder.Property(t => t.Notes).HasColumnName("notes");
        builder.Property(t => t.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(t => t.CreatedBy).HasColumnName("created_by");

        // Relations
        builder.HasOne(t => t.HomeGame)
            .WithMany(h => h.Tournaments)
            .HasForeignKey(t => t.HomeGameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Ranking)
            .WithMany(r => r.Tournaments)
            .HasForeignKey(t => t.RankingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.BlindStructure)
            .WithMany()
            .HasForeignKey(t => t.BlindStructureId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(t => t.Entries)
            .WithOne(e => e.Tournament)
            .HasForeignKey(e => e.TournamentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Tables)
            .WithOne(tb => tb.Tournament)
            .HasForeignKey(tb => tb.TournamentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Prizes)
            .WithOne(p => p.Tournament)
            .HasForeignKey(p => p.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.CostExtras)
            .WithOne(c => c.Tournament)
            .HasForeignKey(c => c.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Eliminations)
            .WithOne(el => el.Tournament)
            .HasForeignKey(el => el.TournamentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(t => t.HomeGameId).HasDatabaseName("ix_tournaments_home_game_id");
        builder.HasIndex(t => t.RankingId).HasDatabaseName("ix_tournaments_ranking_id");
        builder.HasIndex(t => t.Status).HasDatabaseName("ix_tournaments_status");
        builder.HasIndex(t => t.Date).HasDatabaseName("ix_tournaments_date");

        builder.HasIndex(t => t.DeletedAt)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_tournaments_not_deleted");
    }
}
