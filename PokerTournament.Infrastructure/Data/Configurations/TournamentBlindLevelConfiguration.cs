using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class TournamentBlindLevelConfiguration : IEntityTypeConfiguration<TournamentBlindLevel>
{
    public void Configure(EntityTypeBuilder<TournamentBlindLevel> builder)
    {
        builder.ToTable("tournament_blind_levels");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        builder.Property(l => l.xmin).IsRowVersion();

        builder.Property(l => l.TournamentId).HasColumnName("tournament_id");
        builder.Property(l => l.LevelNumber).HasColumnName("level_number");
        builder.Property(l => l.SmallBlind).HasColumnName("small_blind");
        builder.Property(l => l.BigBlind).HasColumnName("big_blind");
        builder.Property(l => l.Ante).HasColumnName("ante");
        builder.Property(l => l.BigBlindAnte).HasColumnName("big_blind_ante");
        builder.Property(l => l.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(l => l.IsBreak).HasColumnName("is_break");
        builder.Property(l => l.BreakDescription).HasColumnName("break_description");

        builder.HasOne(l => l.Tournament)
            .WithMany(t => t.BlindLevels)
            .HasForeignKey(l => l.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.TournamentId, l.LevelNumber })
            .IsUnique()
            .HasDatabaseName("ix_tournament_blind_levels_tournament_level");
    }
}
