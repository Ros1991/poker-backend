using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class TournamentEntryConfiguration : IEntityTypeConfiguration<TournamentEntry>
{
    public void Configure(EntityTypeBuilder<TournamentEntry> builder)
    {
        builder.ToTable("tournament_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.xmin)
            .IsRowVersion();

        builder.Property(e => e.TournamentId).HasColumnName("tournament_id");
        builder.Property(e => e.PersonId).HasColumnName("person_id");
        builder.Property(e => e.TableId).HasColumnName("table_id");
        builder.Property(e => e.SeatNumber).HasColumnName("seat_number");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(50);

        // Buy-in
        builder.Property(e => e.BuyInPaid).HasColumnName("buy_in_paid");
        builder.Property(e => e.BuyInAmount).HasColumnName("buy_in_amount").HasColumnType("decimal(10,2)");

        // Rebuy
        builder.Property(e => e.RebuyCount).HasColumnName("rebuy_count");
        builder.Property(e => e.RebuyTotal).HasColumnName("rebuy_total").HasColumnType("decimal(10,2)");

        // Addon
        builder.Property(e => e.AddonPurchased).HasColumnName("addon_purchased");
        builder.Property(e => e.AddonDouble).HasColumnName("addon_double").HasDefaultValue(false);
        builder.Property(e => e.AddonAmount).HasColumnName("addon_amount").HasColumnType("decimal(10,2)");

        // Totals
        builder.Property(e => e.TotalDue).HasColumnName("total_due").HasColumnType("decimal(10,2)");
        builder.Property(e => e.TotalPaid).HasColumnName("total_paid").HasColumnType("decimal(10,2)");
        builder.Property(e => e.Balance).HasColumnName("balance").HasColumnType("decimal(10,2)");

        builder.Property(e => e.PaymentStatus)
            .HasColumnName("payment_status")
            .IsRequired()
            .HasMaxLength(50);

        // Elimination
        builder.Property(e => e.FinalPosition).HasColumnName("final_position");
        builder.Property(e => e.EliminatedAt).HasColumnName("eliminated_at");
        builder.Property(e => e.EliminatedById).HasColumnName("eliminated_by_id");
        builder.Property(e => e.EliminationTableId).HasColumnName("elimination_table_id");
        builder.Property(e => e.EliminationSeatNumber).HasColumnName("elimination_seat_number");

        // Re-entry
        builder.Property(e => e.IsReentry).HasColumnName("is_reentry");
        builder.Property(e => e.OriginalEntryId).HasColumnName("original_entry_id");
        builder.Property(e => e.EntryNumber).HasColumnName("entry_number").HasDefaultValue(1);

        // Prize
        builder.Property(e => e.PrizeAmount).HasColumnName("prize_amount").HasColumnType("decimal(10,2)");
        builder.Property(e => e.PrizePaid).HasColumnName("prize_paid");

        // Ranking
        builder.Property(e => e.RankingPoints).HasColumnName("ranking_points").HasColumnType("decimal(10,2)");

        // Registration
        builder.Property(e => e.RegisteredAt).HasColumnName("registered_at");
        builder.Property(e => e.RegisteredBy).HasColumnName("registered_by");
        builder.Property(e => e.Notes).HasColumnName("notes");

        // Relations
        builder.HasOne(e => e.Tournament)
            .WithMany(t => t.Entries)
            .HasForeignKey(e => e.TournamentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Person)
            .WithMany(p => p.TournamentEntries)
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Table)
            .WithMany(tb => tb.Entries)
            .HasForeignKey(e => e.TableId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.EliminatedByEntry)
            .WithMany()
            .HasForeignKey(e => e.EliminatedById)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Transactions)
            .WithOne(tx => tx.Entry)
            .HasForeignKey(tx => tx.EntryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Unique constraint: (tournament_id, person_id, entry_number)
        builder.HasIndex(e => new { e.TournamentId, e.PersonId, e.EntryNumber })
            .IsUnique()
            .HasDatabaseName("ix_tournament_entries_tournament_person_entry");

        // Indexes
        builder.HasIndex(e => e.TournamentId).HasDatabaseName("ix_tournament_entries_tournament_id");
        builder.HasIndex(e => e.PersonId).HasDatabaseName("ix_tournament_entries_person_id");
        builder.HasIndex(e => e.Status).HasDatabaseName("ix_tournament_entries_status");
        builder.HasIndex(e => e.TableId).HasDatabaseName("ix_tournament_entries_table_id");
        builder.HasIndex(e => e.FinalPosition).HasDatabaseName("ix_tournament_entries_position");
    }
}
