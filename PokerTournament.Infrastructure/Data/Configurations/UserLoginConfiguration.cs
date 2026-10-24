using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

/// <summary>Logins externos (SPEC-20261023-1210): provedor + subject únicos → usuário.</summary>
public class UserLoginConfiguration : IEntityTypeConfiguration<UserLogin>
{
    public void Configure(EntityTypeBuilder<UserLogin> builder)
    {
        builder.ToTable("user_logins");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        builder.Property(l => l.xmin).IsRowVersion();

        builder.Property(l => l.UserId).HasColumnName("user_id");
        builder.Property(l => l.Provider)
            .HasColumnName("provider")
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(l => l.Subject)
            .HasColumnName("subject")
            .IsRequired()
            .HasMaxLength(255);
        builder.Property(l => l.Email)
            .HasColumnName("email")
            .HasMaxLength(200);
        builder.Property(l => l.LastUsedAt).HasColumnName("last_used_at");

        builder.HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // A identidade é o sub do provedor: um login pertence a UMA conta
        builder.HasIndex(l => new { l.Provider, l.Subject })
            .IsUnique()
            .HasDatabaseName("ux_user_logins_provider_subject");
        builder.HasIndex(l => l.UserId).HasDatabaseName("ix_user_logins_user_id");
    }
}
