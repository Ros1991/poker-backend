using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class TournamentTableConfiguration : IEntityTypeConfiguration<TournamentTable>
{
    public void Configure(EntityTypeBuilder<TournamentTable> builder)
    {
        builder.ToTable("tournament_tables");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.xmin)
            .IsRowVersion();

        builder.Property(t => t.TournamentId).HasColumnName("tournament_id");
        builder.Property(t => t.TableNumber).HasColumnName("table_number");

        builder.Property(t => t.TableName)
            .HasColumnName("table_name")
            .HasMaxLength(100);

        builder.Property(t => t.MaxSeats).HasColumnName("max_seats");
        builder.Property(t => t.DealerPersonId).HasColumnName("dealer_person_id");
        builder.Property(t => t.IsFinalTable).HasColumnName("is_final_table");
        builder.Property(t => t.IsActive).HasColumnName("is_active").HasDefaultValue(true);

        builder.HasOne(t => t.Tournament)
            .WithMany(tour => tour.Tables)
            .HasForeignKey(t => t.TournamentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.DealerPerson)
            .WithMany()
            .HasForeignKey(t => t.DealerPersonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(t => t.Entries)
            .WithOne(e => e.Table)
            .HasForeignKey(e => e.TableId)
            .OnDelete(DeleteBehavior.SetNull);

        // Unique: (tournament_id, table_number)
        builder.HasIndex(t => new { t.TournamentId, t.TableNumber })
            .IsUnique()
            .HasDatabaseName("ix_tournament_tables_tournament_number");
    }
}
