using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class TournamentPrizeConfiguration : IEntityTypeConfiguration<TournamentPrize>
{
    public void Configure(EntityTypeBuilder<TournamentPrize> builder)
    {
        builder.ToTable("tournament_prizes");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.xmin)
            .IsRowVersion();

        builder.Property(p => p.TournamentId).HasColumnName("tournament_id");
        builder.Property(p => p.Position).HasColumnName("position");
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)");
        builder.Property(p => p.Percentage).HasColumnName("percentage").HasColumnType("decimal(5,2)");
        builder.Property(p => p.EntryId).HasColumnName("entry_id");
        builder.Property(p => p.Paid).HasColumnName("paid");
        builder.Property(p => p.PaidAt).HasColumnName("paid_at");

        builder.Property(p => p.PaymentMethod)
            .HasColumnName("payment_method")
            .HasMaxLength(50);

        builder.HasOne(p => p.Tournament)
            .WithMany(t => t.Prizes)
            .HasForeignKey(p => p.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Entry)
            .WithMany()
            .HasForeignKey(p => p.EntryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Unique: (tournament_id, position)
        builder.HasIndex(p => new { p.TournamentId, p.Position })
            .IsUnique()
            .HasDatabaseName("ix_tournament_prizes_tournament_position");
    }
}
