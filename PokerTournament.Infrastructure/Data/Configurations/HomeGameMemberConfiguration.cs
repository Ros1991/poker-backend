using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class HomeGameMemberConfiguration : IEntityTypeConfiguration<HomeGameMember>
{
    public void Configure(EntityTypeBuilder<HomeGameMember> builder)
    {
        builder.ToTable("home_game_members");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        builder.Property(m => m.xmin)
            .IsRowVersion();

        builder.Property(m => m.HomeGameId).HasColumnName("home_game_id");
        builder.Property(m => m.UserId).HasColumnName("user_id");
        builder.Property(m => m.PersonId).HasColumnName("person_id");

        builder.Property(m => m.IsAdmin)
            .HasColumnName("is_admin")
            .HasDefaultValue(false);

        builder.Property(m => m.JoinedAt).HasColumnName("joined_at");

        builder.Property(m => m.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.HasOne(m => m.HomeGame)
            .WithMany(h => h.Members)
            .HasForeignKey(m => m.HomeGameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.User)
            .WithMany(u => u.HomeGameMemberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Person)
            .WithMany(p => p.HomeGameMembers)
            .HasForeignKey(m => m.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique user per home_game
        builder.HasIndex(m => new { m.HomeGameId, m.UserId })
            .IsUnique()
            .HasDatabaseName("ix_home_game_members_home_game_user");
    }
}
