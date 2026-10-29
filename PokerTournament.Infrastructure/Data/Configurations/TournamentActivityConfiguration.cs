using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class TournamentActivityConfiguration : IEntityTypeConfiguration<TournamentActivity>
{
    public void Configure(EntityTypeBuilder<TournamentActivity> builder)
    {
        builder.ToTable("tournament_activities");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.Property(a => a.xmin).IsRowVersion();

        builder.Property(a => a.TournamentId).HasColumnName("tournament_id");
        builder.Property(a => a.EntryId).HasColumnName("entry_id");
        builder.Property(a => a.Type).HasColumnName("type").IsRequired().HasMaxLength(50);
        builder.Property(a => a.Description).HasColumnName("description").IsRequired().HasMaxLength(1000);
        builder.Property(a => a.CreatedBy).HasColumnName("created_by");
        builder.Property(a => a.PlayerOnly).HasColumnName("player_only").HasDefaultValue(false);
        // Timebank (SPEC-20261029-1722): variação do contador e o desfazer pelo histórico
        builder.Property(a => a.Delta).HasColumnName("delta");
        builder.Property(a => a.UndoneAt).HasColumnName("undone_at");
        builder.Property(a => a.UndoneBy).HasColumnName("undone_by");

        builder.HasOne<Tournament>()
            .WithMany()
            .HasForeignKey(a => a.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Sem FK para a inscrição: ela pode ser apagada (inscrição desfeita) e o registro fica.
        builder.HasIndex(a => new { a.TournamentId, a.CreatedAt })
            .HasDatabaseName("ix_tournament_activities_tournament_created");
        builder.HasIndex(a => a.EntryId).HasDatabaseName("ix_tournament_activities_entry_id");
    }
}
