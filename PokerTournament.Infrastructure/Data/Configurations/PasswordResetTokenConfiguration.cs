using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.xmin).IsRowVersion();

        builder.Property(t => t.UserId).HasColumnName("user_id");
        builder.Property(t => t.TokenHash)
            .HasColumnName("token_hash")
            .IsRequired()
            .HasMaxLength(64);
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at");
        builder.Property(t => t.UsedAt).HasColumnName("used_at");
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");
        builder.Property(t => t.RequestedIp).HasColumnName("requested_ip").HasMaxLength(45);
        builder.Property(t => t.UserAgent).HasColumnName("user_agent").HasMaxLength(300);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_password_reset_tokens_token_hash");
        builder.HasIndex(t => t.UserId).HasDatabaseName("ix_password_reset_tokens_user_id");
    }
}
