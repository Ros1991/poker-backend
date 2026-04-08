using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class CostExtraConfiguration : IEntityTypeConfiguration<CostExtra>
{
    public void Configure(EntityTypeBuilder<CostExtra> builder)
    {
        builder.ToTable("cost_extras");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.xmin)
            .IsRowVersion();

        builder.Property(c => c.TournamentId).HasColumnName("tournament_id");

        builder.Property(c => c.Description)
            .HasColumnName("description")
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(c => c.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)");

        builder.Property(c => c.Beneficiary)
            .HasColumnName("beneficiary")
            .HasMaxLength(200);

        builder.Property(c => c.PixKey).HasColumnName("pix_key").HasMaxLength(200);
        builder.Property(c => c.PixKeyType).HasColumnName("pix_key_type").HasMaxLength(50);
        builder.Property(c => c.IsCashBox).HasColumnName("is_cash_box");
        builder.Property(c => c.PaidAmount).HasColumnName("paid_amount").HasColumnType("decimal(10,2)");

        builder.Property(c => c.PaymentStatus)
            .HasColumnName("payment_status")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(c => c.PaymentMethod).HasColumnName("payment_method").HasMaxLength(30);
        builder.Property(c => c.PaidAt).HasColumnName("paid_at");
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.Notes).HasColumnName("notes");

        builder.HasOne(c => c.Tournament)
            .WithMany(t => t.CostExtras)
            .HasForeignKey(c => c.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique index on (tournament_id) where is_cash_box = true
        builder.HasIndex(c => c.TournamentId)
            .IsUnique()
            .HasFilter("is_cash_box = true")
            .HasDatabaseName("ix_cost_extras_tournament_cash_box");

        builder.HasIndex(c => c.TournamentId)
            .HasDatabaseName("ix_cost_extras_tournament_id");
    }
}
