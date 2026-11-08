using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

/// <summary>Tabela <c>tournament_invites</c> (SPEC-20261108-1632): o convite (QR/link) do torneio, um ativo por torneio.</summary>
public class TournamentInviteConfiguration : IEntityTypeConfiguration<TournamentInvite>
{
    public void Configure(EntityTypeBuilder<TournamentInvite> builder)
    {
        builder.ToTable("tournament_invites");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");
        builder.Property(i => i.xmin).IsRowVersion();

        builder.Property(i => i.TournamentId).HasColumnName("tournament_id");
        builder.Property(i => i.Code).HasColumnName("code").IsRequired().HasMaxLength(16);
        builder.Property(i => i.ExpiresAt).HasColumnName("expires_at");
        builder.Property(i => i.RevokedAt).HasColumnName("revoked_at");
        builder.Property(i => i.CreatedBy).HasColumnName("created_by");

        builder.HasOne<Tournament>()
            .WithMany()
            .HasForeignKey(i => i.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        // O código vai no QR/link: a leitura anônima é por ele
        builder.HasIndex(i => i.Code).IsUnique().HasDatabaseName("ix_tournament_invites_code");
        builder.HasIndex(i => i.TournamentId).HasDatabaseName("ix_tournament_invites_tournament_id");
    }
}

/// <summary>Tabela <c>player_link_requests</c> (SPEC-20261108-1632): pedidos "sou eu" aguardando o organizador.</summary>
public class PlayerLinkRequestConfiguration : IEntityTypeConfiguration<PlayerLinkRequest>
{
    public void Configure(EntityTypeBuilder<PlayerLinkRequest> builder)
    {
        builder.ToTable("player_link_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.xmin).IsRowVersion();

        builder.Property(r => r.TournamentId).HasColumnName("tournament_id");
        builder.Property(r => r.PersonId).HasColumnName("person_id");
        builder.Property(r => r.UserId).HasColumnName("user_id");
        builder.Property(r => r.InviteId).HasColumnName("invite_id");
        builder.Property(r => r.Status).HasColumnName("status").IsRequired().HasMaxLength(20);
        builder.Property(r => r.Message).HasColumnName("message").HasMaxLength(300);
        builder.Property(r => r.RequestedAt).HasColumnName("requested_at");
        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at");
        builder.Property(r => r.DecidedAt).HasColumnName("decided_at");
        builder.Property(r => r.DecidedBy).HasColumnName("decided_by");
        builder.Property(r => r.EmailCopied).HasColumnName("email_copied").HasDefaultValue(false);

        builder.HasOne<Tournament>()
            .WithMany()
            .HasForeignKey(r => r.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);
        // Jogador excluído do cadastro (soft delete) continua no pedido; a FK só cai no hard delete
        builder.HasOne(r => r.Person)
            .WithMany()
            .HasForeignKey(r => r.PersonId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Lista do organizador (pendentes do torneio) e o Início (pendentes dos home games que a conta gerencia)
        builder.HasIndex(r => new { r.TournamentId, r.Status })
            .HasDatabaseName("ix_player_link_requests_tournament_id_status");
        // "Meu pedido" na página do convite e o vínculo de origem ao soltar
        builder.HasIndex(r => new { r.UserId, r.TournamentId })
            .HasDatabaseName("ix_player_link_requests_user_id_tournament_id");
        builder.HasIndex(r => r.PersonId).HasDatabaseName("ix_player_link_requests_person_id");
        // Um pedido pendente por conta e torneio: dois envios simultâneos não viram dois pedidos
        builder.HasIndex(r => new { r.TournamentId, r.UserId })
            .IsUnique()
            .HasFilter("status = 'Pending'")
            .HasDatabaseName("ux_player_link_requests_pending_user");
    }
}
