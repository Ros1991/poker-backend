using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class BlindStructureConfiguration : IEntityTypeConfiguration<BlindStructure>
{
    public void Configure(EntityTypeBuilder<BlindStructure> builder)
    {
        builder.ToTable("blind_structures");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");
        builder.Property(b => b.CreatedAt).HasColumnName("created_at");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at");
        builder.Property(b => b.xmin)
            .IsRowVersion();

        builder.Property(b => b.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(b => b.Description).HasColumnName("description");
        builder.Property(b => b.HomeGameId).HasColumnName("home_game_id");

        builder.Property(b => b.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        // Template defaults
        builder.Property(b => b.DefaultBuyIn)
            .HasColumnName("default_buy_in")
            .HasColumnType("decimal(10,2)");

        builder.Property(b => b.DefaultRebuy)
            .HasColumnName("default_rebuy")
            .HasColumnType("decimal(10,2)");

        builder.Property(b => b.DefaultAddon)
            .HasColumnName("default_addon")
            .HasColumnType("decimal(10,2)");

        builder.Property(b => b.DefaultStartingStack)
            .HasColumnName("default_starting_stack");

        builder.Property(b => b.DefaultRebuyStack)
            .HasColumnName("default_rebuy_stack");

        builder.Property(b => b.DefaultAddonStack)
            .HasColumnName("default_addon_stack");

        builder.Property(b => b.DefaultMaxRebuys)
            .HasColumnName("default_max_rebuys");

        builder.Property(b => b.DefaultAddonAllowed)
            .HasColumnName("default_addon_allowed")
            .HasDefaultValue(true);

        builder.Property(b => b.DefaultAddonDoubleAllowed)
            .HasColumnName("default_addon_double_allowed")
            .HasDefaultValue(false);

        builder.Property(b => b.DefaultLateRegistrationLevel)
            .HasColumnName("default_late_registration_level");

        builder.Property(b => b.DefaultRebuyUntilLevel)
            .HasColumnName("default_rebuy_until_level");

        builder.Property(b => b.DefaultSeatsPerTable)
            .HasColumnName("default_seats_per_table");

        builder.HasOne(b => b.HomeGame)
            .WithMany()
            .HasForeignKey(b => b.HomeGameId)
            .OnDelete(DeleteBehavior.SetNull);

        // Cascade delete levels when structure deleted
        builder.HasMany(b => b.Levels)
            .WithOne(l => l.BlindStructure)
            .HasForeignKey(l => l.BlindStructureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.HomeGameId)
            .HasDatabaseName("ix_blind_structures_home_game_id");
    }
}

public class BlindLevelConfiguration : IEntityTypeConfiguration<BlindLevel>
{
    public void Configure(EntityTypeBuilder<BlindLevel> builder)
    {
        builder.ToTable("blind_levels");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        builder.Property(l => l.xmin)
            .IsRowVersion();

        builder.Property(l => l.BlindStructureId).HasColumnName("blind_structure_id");
        builder.Property(l => l.LevelNumber).HasColumnName("level_number");
        builder.Property(l => l.SmallBlind).HasColumnName("small_blind");
        builder.Property(l => l.BigBlind).HasColumnName("big_blind");
        builder.Property(l => l.Ante).HasColumnName("ante");
        builder.Property(l => l.BigBlindAnte).HasColumnName("big_blind_ante");
        builder.Property(l => l.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(l => l.IsBreak).HasColumnName("is_break");
        builder.Property(l => l.BreakDescription).HasColumnName("break_description");

        builder.HasOne(l => l.BlindStructure)
            .WithMany(b => b.Levels)
            .HasForeignKey(l => l.BlindStructureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.BlindStructureId, l.LevelNumber })
            .IsUnique()
            .HasDatabaseName("ix_blind_levels_structure_level");
    }
}
