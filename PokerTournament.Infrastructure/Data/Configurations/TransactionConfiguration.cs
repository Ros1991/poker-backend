using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.xmin)
            .IsRowVersion();

        builder.Property(t => t.TournamentId).HasColumnName("tournament_id");
        builder.Property(t => t.EntryId).HasColumnName("entry_id");

        builder.Property(t => t.Type)
            .HasColumnName("type")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)");
        builder.Property(t => t.Description).HasColumnName("description");

        builder.Property(t => t.PaymentMethod)
            .HasColumnName("payment_method")
            .HasMaxLength(50);

        builder.Property(t => t.PixAmount).HasColumnName("pix_amount").HasColumnType("decimal(10,2)");
        builder.Property(t => t.CashAmount).HasColumnName("cash_amount").HasColumnType("decimal(10,2)");
        builder.Property(t => t.PixDestinationId).HasColumnName("pix_destination_id");
        builder.Property(t => t.CreatedBy).HasColumnName("created_by");
        builder.Property(t => t.ReversedAt).HasColumnName("reversed_at");
        builder.Property(t => t.ReversedBy).HasColumnName("reversed_by");
        builder.Property(t => t.ReversalReason).HasColumnName("reversal_reason");

        // Relations
        builder.HasOne(t => t.Tournament)
            .WithMany()
            .HasForeignKey(t => t.TournamentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Entry)
            .WithMany(e => e.Transactions)
            .HasForeignKey(t => t.EntryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.PixDestination)
            .WithMany()
            .HasForeignKey(t => t.PixDestinationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => t.TournamentId).HasDatabaseName("ix_transactions_tournament_id");
        builder.HasIndex(t => t.EntryId).HasDatabaseName("ix_transactions_entry_id");
    }
}
