using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

/// <summary>Tabela <c>tournament_join_requests</c> (SPEC-20261108-1639): pedidos de vaga, reservas e lista de espera.</summary>
public class TournamentJoinRequestConfiguration : IEntityTypeConfiguration<TournamentJoinRequest>
{
    public void Configure(EntityTypeBuilder<TournamentJoinRequest> builder)
    {
        builder.ToTable("tournament_join_requests", t =>
        {
            t.HasCheckConstraint("ck_tournament_join_requests_status",
                "status IN ('Pending','Approved','Waitlisted','Declined','Cancelled')");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.xmin).IsRowVersion();

        builder.Property(r => r.TournamentId).HasColumnName("tournament_id");
        builder.Property(r => r.UserId).HasColumnName("user_id");
        builder.Property(r => r.Message).HasColumnName("message").HasMaxLength(TournamentJoinRequest.MessageMaxLength);
        builder.Property(r => r.Status).HasColumnName("status").IsRequired().HasMaxLength(20);
        builder.Property(r => r.WaitlistPosition).HasColumnName("waitlist_position");
        builder.Property(r => r.PersonId).HasColumnName("person_id");
        builder.Property(r => r.EntryId).HasColumnName("entry_id");
        builder.Property(r => r.RequestedAt).HasColumnName("requested_at");
        builder.Property(r => r.DecidedAt).HasColumnName("decided_at");
        builder.Property(r => r.DecidedBy).HasColumnName("decided_by");

        builder.HasOne(r => r.Tournament)
            .WithMany()
            .HasForeignKey(r => r.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        // Jogador excluído do cadastro (soft delete) continua no pedido; a FK só cai no hard delete
        builder.HasOne(r => r.Person)
            .WithMany()
            .HasForeignKey(r => r.PersonId)
            .OnDelete(DeleteBehavior.SetNull);
        // Inscrição desfeita (hard delete da TournamentEntry): a reserva volta para a fila
        builder.HasOne(r => r.Entry)
            .WithMany()
            .HasForeignKey(r => r.EntryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Painel (pedidos e reservas do torneio) e terminal (reservas)
        builder.HasIndex(r => new { r.TournamentId, r.Status })
            .HasDatabaseName("ix_tournament_join_requests_tournament_id_status");
        // "Meus torneios › Próximos" e o estado do botão da página pública
        builder.HasIndex(r => new { r.UserId, r.Status })
            .HasDatabaseName("ix_tournament_join_requests_user_id_status");
        builder.HasIndex(r => r.PersonId).HasDatabaseName("ix_tournament_join_requests_person_id");
        builder.HasIndex(r => r.EntryId).HasDatabaseName("ix_tournament_join_requests_entry_id");
        // Um pedido ABERTO por conta e torneio: dois toques simultâneos não viram dois pedidos
        builder.HasIndex(r => new { r.TournamentId, r.UserId })
            .IsUnique()
            .HasFilter("status IN ('Pending','Approved','Waitlisted')")
            .HasDatabaseName("ux_tournament_join_requests_open");
    }
}

/// <summary>Tabela <c>tournament_reminders</c> (SPEC-20261108-1639): lembrete do dia enviado, uma linha por conta e torneio.</summary>
public class TournamentReminderConfiguration : IEntityTypeConfiguration<TournamentReminder>
{
    public void Configure(EntityTypeBuilder<TournamentReminder> builder)
    {
        builder.ToTable("tournament_reminders");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.xmin).IsRowVersion();

        builder.Property(r => r.TournamentId).HasColumnName("tournament_id");
        builder.Property(r => r.UserId).HasColumnName("user_id");
        builder.Property(r => r.SentAt).HasColumnName("sent_at");

        builder.HasOne<Tournament>()
            .WithMany()
            .HasForeignKey(r => r.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.TournamentId, r.UserId })
            .IsUnique()
            .HasDatabaseName("ux_tournament_reminders_tournament_user");
    }
}
