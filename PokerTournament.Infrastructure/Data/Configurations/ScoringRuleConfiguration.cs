using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class ScoringRuleConfiguration : IEntityTypeConfiguration<ScoringRule>
{
    public void Configure(EntityTypeBuilder<ScoringRule> builder)
    {
        builder.ToTable("scoring_rules");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.xmin)
            .IsRowVersion();

        builder.Property(s => s.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Description).HasColumnName("description");
        builder.Property(s => s.HomeGameId).HasColumnName("home_game_id");

        builder.Property(s => s.PointsConfig)
            .HasColumnName("points_config")
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(s => s.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.HasOne(s => s.HomeGame)
            .WithMany()
            .HasForeignKey(s => s.HomeGameId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => s.HomeGameId)
            .HasDatabaseName("ix_scoring_rules_home_game_id");
    }
}
