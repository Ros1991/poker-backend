using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.xmin).IsRowVersion();

        builder.Property(s => s.UserId).HasColumnName("user_id");
        builder.Property(s => s.RefreshTokenHash)
            .HasColumnName("refresh_token_hash")
            .IsRequired()
            .HasMaxLength(64);
        builder.Property(s => s.ExpiresAt).HasColumnName("expires_at");
        builder.Property(s => s.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(s => s.RevokedAt).HasColumnName("revoked_at");
        builder.Property(s => s.UserAgent).HasColumnName("user_agent").HasMaxLength(300);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.RefreshTokenHash)
            .IsUnique()
            .HasDatabaseName("ix_auth_sessions_refresh_token_hash");
        builder.HasIndex(s => s.UserId).HasDatabaseName("ix_auth_sessions_user_id");
    }
}

public class DisplaySessionConfiguration : IEntityTypeConfiguration<DisplaySession>
{
    public void Configure(EntityTypeBuilder<DisplaySession> builder)
    {
        builder.ToTable("display_sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.xmin).IsRowVersion();

        builder.Property(s => s.DeviceTokenHash)
            .HasColumnName("device_token_hash")
            .IsRequired()
            .HasMaxLength(64);
        builder.Property(s => s.PairingCode).HasColumnName("pairing_code").HasMaxLength(6);
        builder.Property(s => s.CodeExpiresAt).HasColumnName("code_expires_at");
        builder.Property(s => s.PreviousPairingCode).HasColumnName("previous_pairing_code").HasMaxLength(6);
        builder.Property(s => s.PreviousCodeExpiresAt).HasColumnName("previous_code_expires_at");
        builder.Property(s => s.Status).HasColumnName("status").IsRequired().HasMaxLength(20);
        builder.Property(s => s.TournamentId).HasColumnName("tournament_id");
        builder.Property(s => s.PairedByUserId).HasColumnName("paired_by_user_id");
        builder.Property(s => s.PairedAt).HasColumnName("paired_at");
        builder.Property(s => s.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(s => s.UserAgent).HasColumnName("user_agent").HasMaxLength(300);

        // Torneio excluído/apagado: a TV volta para o QR (sem cascata que apague a sessão)
        builder.HasOne<Tournament>()
            .WithMany()
            .HasForeignKey(s => s.TournamentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => s.DeviceTokenHash)
            .IsUnique()
            .HasDatabaseName("ix_display_sessions_device_token_hash");
        // Código só precisa ser único enquanto está em uso
        builder.HasIndex(s => s.PairingCode)
            .IsUnique()
            .HasFilter("pairing_code IS NOT NULL")
            .HasDatabaseName("ix_display_sessions_pairing_code");
        // Busca pelo código anterior (lookup/claim). Sem unicidade: a geração já evita
        // repetir código atual/anterior, e uma corrida rara não deve virar erro na renovação
        builder.HasIndex(s => s.PreviousPairingCode)
            .HasFilter("previous_pairing_code IS NOT NULL")
            .HasDatabaseName("ix_display_sessions_previous_pairing_code");
    }
}

public class DealerAccessConfiguration : IEntityTypeConfiguration<DealerAccess>
{
    public void Configure(EntityTypeBuilder<DealerAccess> builder)
    {
        builder.ToTable("dealer_accesses");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.Property(a => a.xmin).IsRowVersion();

        builder.Property(a => a.TournamentId).HasColumnName("tournament_id");
        builder.Property(a => a.Code).HasColumnName("code").IsRequired().HasMaxLength(32);
        builder.Property(a => a.ExpiresAt).HasColumnName("expires_at");
        builder.Property(a => a.RevokedAt).HasColumnName("revoked_at");
        builder.Property(a => a.CreatedBy).HasColumnName("created_by");
        builder.Property(a => a.LastExchangeAt).HasColumnName("last_exchange_at");
        builder.Property(a => a.ExchangeCount).HasColumnName("exchange_count").HasDefaultValue(0);

        builder.HasOne<Tournament>()
            .WithMany()
            .HasForeignKey(a => a.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.Code).IsUnique().HasDatabaseName("ix_dealer_accesses_code");
        builder.HasIndex(a => a.TournamentId).HasDatabaseName("ix_dealer_accesses_tournament_id");
    }
}
