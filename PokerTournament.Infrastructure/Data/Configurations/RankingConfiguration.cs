using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class RankingConfiguration : IEntityTypeConfiguration<Ranking>
{
    public void Configure(EntityTypeBuilder<Ranking> builder)
    {
        builder.ToTable("rankings");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.DeletedAt).HasColumnName("deleted_at");
        builder.Property(r => r.xmin)
            .IsRowVersion();

        builder.Property(r => r.HomeGameId).HasColumnName("home_game_id");
        builder.Property(r => r.ScoringRuleId).HasColumnName("scoring_rule_id");

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.Description).HasColumnName("description");

        builder.Property(r => r.Season)
            .HasColumnName("season")
            .HasMaxLength(50);

        builder.Property(r => r.StartDate).HasColumnName("start_date");
        builder.Property(r => r.EndDate).HasColumnName("end_date");

        builder.Property(r => r.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(r => r.ScoringMode)
            .HasColumnName("scoring_mode")
            .HasMaxLength(20)
            .HasDefaultValue("Formula");

        builder.Property(r => r.ScoringFormula)
            .HasColumnName("scoring_formula")
            .HasMaxLength(500);

        builder.Property(r => r.ScoringTable)
            .HasColumnName("scoring_table")
            .HasColumnType("text");

        builder.Property(r => r.AccumulatedPrize)
            .HasColumnName("accumulated_prize")
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(0m);

        builder.HasOne(r => r.HomeGame)
            .WithMany(h => h.Rankings)
            .HasForeignKey(r => r.HomeGameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ScoringRule)
            .WithMany()
            .HasForeignKey(r => r.ScoringRuleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(r => r.Scores)
            .WithOne(s => s.Ranking)
            .HasForeignKey(s => s.RankingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Tournaments)
            .WithOne(t => t.Ranking)
            .HasForeignKey(t => t.RankingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(r => r.HomeGameId)
            .HasDatabaseName("ix_rankings_home_game_id");

        builder.HasIndex(r => r.DeletedAt)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_rankings_not_deleted");
    }
}

public class RankingScoreConfiguration : IEntityTypeConfiguration<RankingScore>
{
    public void Configure(EntityTypeBuilder<RankingScore> builder)
    {
        builder.ToTable("ranking_scores");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.xmin)
            .IsRowVersion();

        builder.Property(s => s.RankingId).HasColumnName("ranking_id");
        builder.Property(s => s.PersonId).HasColumnName("person_id");
        builder.Property(s => s.TournamentId).HasColumnName("tournament_id");
        builder.Property(s => s.EntryId).HasColumnName("entry_id");
        builder.Property(s => s.Position).HasColumnName("position");

        builder.Property(s => s.Points)
            .HasColumnName("points")
            .HasColumnType("decimal(10,2)");

        builder.Property(s => s.BonusPoints)
            .HasColumnName("bonus_points")
            .HasColumnType("decimal(10,2)");

        builder.Property(s => s.TotalPoints)
            .HasColumnName("total_points")
            .HasColumnType("decimal(10,2)");

        builder.Property(s => s.PlayerCount).HasColumnName("player_count");

        builder.HasOne(s => s.Ranking)
            .WithMany(r => r.Scores)
            .HasForeignKey(s => s.RankingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Person)
            .WithMany()
            .HasForeignKey(s => s.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Tournament)
            .WithMany()
            .HasForeignKey(s => s.TournamentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Entry)
            .WithMany()
            .HasForeignKey(s => s.EntryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Unique: (ranking_id, person_id, tournament_id)
        builder.HasIndex(s => new { s.RankingId, s.PersonId, s.TournamentId })
            .IsUnique()
            .HasDatabaseName("ix_ranking_scores_ranking_person_tournament");
    }
}
