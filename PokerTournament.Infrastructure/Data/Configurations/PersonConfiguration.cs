using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("persons");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");
        builder.Property(p => p.xmin)
            .IsRowVersion();

        builder.Property(p => p.HomeGameId)
            .HasColumnName("home_game_id")
            .IsRequired();

        builder.Property(p => p.Type)
            .HasColumnName("type")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Jogador");

        builder.Property(p => p.FullName)
            .HasColumnName("full_name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Nickname)
            .HasColumnName("nickname")
            .HasMaxLength(100);

        builder.Property(p => p.Email)
            .HasColumnName("email")
            .HasMaxLength(200);

        builder.Property(p => p.Whatsapp)
            .HasColumnName("whatsapp")
            .HasMaxLength(20);

        builder.Property(p => p.PhotoUrl)
            .HasColumnName("photo_url")
            .HasMaxLength(500);

        builder.Property(p => p.Document)
            .HasColumnName("document")
            .HasMaxLength(50);

        builder.Property(p => p.Notes)
            .HasColumnName("notes");

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        // Relationship with HomeGame
        builder.HasOne(p => p.HomeGame)
            .WithMany(h => h.Persons)
            .HasForeignKey(p => p.HomeGameId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique email per home_game where not null and not deleted
        builder.HasIndex(p => new { p.HomeGameId, p.Email })
            .IsUnique()
            .HasFilter("email IS NOT NULL AND deleted_at IS NULL")
            .HasDatabaseName("ix_persons_home_game_email");

        // Unique whatsapp per home_game where not null and not deleted
        builder.HasIndex(p => new { p.HomeGameId, p.Whatsapp })
            .IsUnique()
            .HasFilter("whatsapp IS NOT NULL AND deleted_at IS NULL")
            .HasDatabaseName("ix_persons_home_game_whatsapp");

        // Index on home_game_id and type for filtering
        builder.HasIndex(p => new { p.HomeGameId, p.Type })
            .HasDatabaseName("ix_persons_home_game_type");

        // Index on deleted_at where null
        builder.HasIndex(p => p.DeletedAt)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_persons_not_deleted");
    }
}
