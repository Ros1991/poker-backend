using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class EmailConfirmationTokenConfiguration : IEntityTypeConfiguration<EmailConfirmationToken>
{
    public void Configure(EntityTypeBuilder<EmailConfirmationToken> builder)
    {
        builder.ToTable("email_confirmation_tokens");

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
        // Sem HasDefaultValue: o default "true" das linhas antigas fica só na migration (com default no modelo,
        // um false gravado pelo código viraria o default do banco)
        builder.Property(t => t.EmailSent).HasColumnName("email_sent");
        // Cadastro pendente preso ao token (D16.10) — mesmos tamanhos das colunas de users
        builder.Property(t => t.PendingFullName).HasColumnName("pending_full_name").HasMaxLength(200);
        builder.Property(t => t.PendingNickname).HasColumnName("pending_nickname").HasMaxLength(100);
        builder.Property(t => t.PendingWhatsapp).HasColumnName("pending_whatsapp").HasMaxLength(20);
        builder.Property(t => t.PendingPasswordHash).HasColumnName("pending_password_hash").HasMaxLength(500);
        builder.Property(t => t.UserAgent).HasColumnName("user_agent").HasMaxLength(300);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_email_confirmation_tokens_token_hash");
        builder.HasIndex(t => t.UserId).HasDatabaseName("ix_email_confirmation_tokens_user_id");
    }
}
