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
        builder.Property(a => a.SupersededAt).HasColumnName("superseded_at");
        builder.Property(a => a.CreatedBy).HasColumnName("created_by");

        builder.HasOne<Tournament>()
            .WithMany()
            .HasForeignKey(a => a.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.Code).IsUnique().HasDatabaseName("ix_dealer_accesses_code");
        builder.HasIndex(a => a.TournamentId).HasDatabaseName("ix_dealer_accesses_tournament_id");
    }
}

public class DealerRequestConfiguration : IEntityTypeConfiguration<DealerRequest>
{
    public void Configure(EntityTypeBuilder<DealerRequest> builder)
    {
        builder.ToTable("dealer_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.xmin).IsRowVersion();

        builder.Property(r => r.TournamentId).HasColumnName("tournament_id");
        builder.Property(r => r.DealerAccessId).HasColumnName("dealer_access_id");
        builder.Property(r => r.Name).HasColumnName("name").IsRequired().HasMaxLength(40);
        builder.Property(r => r.DeviceKeyHash)
            .HasColumnName("device_key_hash")
            .IsRequired()
            .HasMaxLength(64);
        builder.Property(r => r.Status).HasColumnName("status").IsRequired().HasMaxLength(20);
        builder.Property(r => r.RequestedAt).HasColumnName("requested_at");
        builder.Property(r => r.DecidedAt).HasColumnName("decided_at");
        builder.Property(r => r.DecidedBy).HasColumnName("decided_by");
        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at");
        builder.Property(r => r.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(r => r.UserAgent).HasColumnName("user_agent").HasMaxLength(300);

        builder.HasOne<Tournament>()
            .WithMany()
            .HasForeignKey(r => r.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DealerAccess>()
            .WithMany()
            .HasForeignKey(r => r.DealerAccessId)
            .OnDelete(DeleteBehavior.Cascade);

        // Lista do organizador e limite de pendentes por torneio
        builder.HasIndex(r => new { r.TournamentId, r.Status })
            .HasDatabaseName("ix_dealer_requests_tournament_id_status");
        // Reaproveitar o pedido aprovado do mesmo aparelho ao reabrir o terminal
        builder.HasIndex(r => r.DeviceKeyHash).HasDatabaseName("ix_dealer_requests_device_key_hash");
        // Um pedido pendente por aparelho e torneio: dois envios simultâneos do mesmo
        // aparelho não viram dois pedidos (o serviço captura a violação e devolve o existente)
        builder.HasIndex(r => new { r.TournamentId, r.DeviceKeyHash })
            .IsUnique()
            .HasFilter("status = 'Pending'")
            .HasDatabaseName("ux_dealer_requests_pending_device");
    }
}
