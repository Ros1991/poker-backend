using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class EliminationConfiguration : IEntityTypeConfiguration<Elimination>
{
    public void Configure(EntityTypeBuilder<Elimination> builder)
    {
        builder.ToTable("eliminations");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.xmin)
            .IsRowVersion();

        builder.Property(e => e.TournamentId).HasColumnName("tournament_id");
        builder.Property(e => e.EntryId).HasColumnName("entry_id");
        builder.Property(e => e.EliminatedByEntryId).HasColumnName("eliminated_by_entry_id");
        builder.Property(e => e.TableId).HasColumnName("table_id");
        builder.Property(e => e.Position).HasColumnName("position");
        builder.Property(e => e.BlindLevel).HasColumnName("blind_level");
        builder.Property(e => e.EliminatedAt).HasColumnName("eliminated_at");
        builder.Property(e => e.Notes).HasColumnName("notes");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.Corrected).HasColumnName("corrected");
        builder.Property(e => e.CorrectedAt).HasColumnName("corrected_at");
        builder.Property(e => e.CorrectedBy).HasColumnName("corrected_by");
        builder.Property(e => e.CorrectionReason).HasColumnName("correction_reason");

        builder.HasOne(e => e.Tournament)
            .WithMany(t => t.Eliminations)
            .HasForeignKey(e => e.TournamentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Entry)
            .WithMany()
            .HasForeignKey(e => e.EntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EliminatedByEntry)
            .WithMany()
            .HasForeignKey(e => e.EliminatedByEntryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Table)
            .WithMany()
            .HasForeignKey(e => e.TableId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.TournamentId).HasDatabaseName("ix_eliminations_tournament_id");
        builder.HasIndex(e => e.EntryId).HasDatabaseName("ix_eliminations_entry_id");
    }
}
