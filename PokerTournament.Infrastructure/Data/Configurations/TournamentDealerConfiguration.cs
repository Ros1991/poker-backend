using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class TournamentDealerConfiguration : IEntityTypeConfiguration<TournamentDealer>
{
    public void Configure(EntityTypeBuilder<TournamentDealer> builder)
    {
        builder.ToTable("tournament_dealers");
        builder.HasKey(td => td.Id);
        builder.Property(td => td.Id).HasColumnName("id");
        builder.Property(td => td.CreatedAt).HasColumnName("created_at");
        builder.Property(td => td.UpdatedAt).HasColumnName("updated_at");
        builder.Property(td => td.xmin).IsRowVersion();
        builder.Property(td => td.TournamentId).HasColumnName("tournament_id");
        builder.Property(td => td.PersonId).HasColumnName("person_id");
        builder.Property(td => td.AssignedAt).HasColumnName("assigned_at");

        builder.HasOne(td => td.Tournament)
            .WithMany(t => t.Dealers)
            .HasForeignKey(td => td.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(td => td.Person)
            .WithMany()
            .HasForeignKey(td => td.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(td => new { td.TournamentId, td.PersonId })
            .IsUnique()
            .HasDatabaseName("ix_tournament_dealers_tournament_person");
    }
}
