using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class HomeGameConfiguration : IEntityTypeConfiguration<HomeGame>
{
    public void Configure(EntityTypeBuilder<HomeGame> builder)
    {
        builder.ToTable("home_games");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnName("id");
        builder.Property(h => h.CreatedAt).HasColumnName("created_at");
        builder.Property(h => h.UpdatedAt).HasColumnName("updated_at");
        builder.Property(h => h.DeletedAt).HasColumnName("deleted_at");
        builder.Property(h => h.xmin)
            .IsRowVersion();

        builder.Property(h => h.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(h => h.Description).HasColumnName("description");
        builder.Property(h => h.Location).HasColumnName("location");
        builder.Property(h => h.LogoUrl).HasColumnName("logo_url");
        builder.Property(h => h.PixKey).HasColumnName("pix_key");
        builder.Property(h => h.PixBeneficiary).HasColumnName("pix_beneficiary");

        builder.Property(h => h.Timezone)
            .HasColumnName("timezone")
            .HasMaxLength(100)
            .HasDefaultValue("America/Sao_Paulo");

        builder.Property(h => h.DefaultBuyIn)
            .HasColumnName("default_buy_in")
            .HasColumnType("decimal(10,2)");

        builder.Property(h => h.DefaultRebuy)
            .HasColumnName("default_rebuy")
            .HasColumnType("decimal(10,2)");

        builder.Property(h => h.DefaultAddon)
            .HasColumnName("default_addon")
            .HasColumnType("decimal(10,2)");

        builder.Property(h => h.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(h => h.OwnerId)
            .HasColumnName("owner_id")
            .IsRequired();

        // Unique name where not deleted
        builder.HasIndex(h => h.Name)
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_home_games_name");

        builder.HasIndex(h => h.DeletedAt)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_home_games_not_deleted");

        // Relations
        builder.HasOne(h => h.Owner)
            .WithMany(u => u.OwnedHomeGames)
            .HasForeignKey(h => h.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(h => h.Members)
            .WithOne(m => m.HomeGame)
            .HasForeignKey(m => m.HomeGameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(h => h.Rankings)
            .WithOne(r => r.HomeGame)
            .HasForeignKey(r => r.HomeGameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(h => h.Tournaments)
            .WithOne(t => t.HomeGame)
            .HasForeignKey(t => t.HomeGameId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
