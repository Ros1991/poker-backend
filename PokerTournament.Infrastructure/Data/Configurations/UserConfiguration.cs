using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        builder.Property(u => u.xmin)
            .IsRowVersion();

        builder.Property(u => u.FullName)
            .HasColumnName("full_name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(u => u.Nickname)
            .HasColumnName("nickname")
            .HasMaxLength(100);

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .IsRequired()
            .HasMaxLength(200);

        // Null = conta só-social (entra pelo Google — SPEC-20261023-1210)
        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(500);

        builder.Property(u => u.Whatsapp)
            .HasColumnName("whatsapp")
            .HasMaxLength(20);

        builder.Property(u => u.PhotoUrl)
            .HasColumnName("photo_url")
            .HasMaxLength(500);

        builder.Property(u => u.Role)
            .HasColumnName("role")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(u => u.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(u => u.EmailConfirmedAt).HasColumnName("email_confirmed_at");
        builder.Property(u => u.RefreshToken).HasColumnName("refresh_token");
        builder.Property(u => u.RefreshTokenExpiresAt).HasColumnName("refresh_token_expires_at");

        // Plano da conta (SPEC-20261023-1205): strings via nameof, default Mesa/Manual para conta nova
        builder.Property(u => u.Plan)
            .HasColumnName("plan")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Mesa");

        builder.Property(u => u.PlanExpiresAt).HasColumnName("plan_expires_at");

        builder.Property(u => u.PlanSource)
            .HasColumnName("plan_source")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Manual");

        builder.Property(u => u.PlanNote)
            .HasColumnName("plan_note")
            .HasMaxLength(200);

        builder.Property(u => u.PlanUpdatedAt).HasColumnName("plan_updated_at");
        builder.Property(u => u.PlanUpdatedBy).HasColumnName("plan_updated_by");

        builder.HasIndex(u => u.Plan)
            .HasDatabaseName("ix_users_plan");

        // Unique email
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("ix_users_email");
    }
}
