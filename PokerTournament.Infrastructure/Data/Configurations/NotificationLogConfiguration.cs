using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("notification_logs");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at");
        builder.Property(n => n.UpdatedAt).HasColumnName("updated_at");
        builder.Property(n => n.xmin)
            .IsRowVersion();

        builder.Property(n => n.PersonId).HasColumnName("person_id");
        builder.Property(n => n.TournamentId).HasColumnName("tournament_id");

        builder.Property(n => n.Channel)
            .HasColumnName("channel")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(n => n.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(20);

        builder.Property(n => n.Message).HasColumnName("message");
        builder.Property(n => n.TemplateKey).HasColumnName("template_key").HasMaxLength(100);

        builder.Property(n => n.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(n => n.SentAt).HasColumnName("sent_at");
        builder.Property(n => n.ErrorMessage).HasColumnName("error_message");

        builder.HasOne(n => n.Person)
            .WithMany()
            .HasForeignKey(n => n.PersonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(n => n.Tournament)
            .WithMany()
            .HasForeignKey(n => n.TournamentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(n => n.PersonId).HasDatabaseName("ix_notification_logs_person_id");
        builder.HasIndex(n => n.TournamentId).HasDatabaseName("ix_notification_logs_tournament_id");
    }
}
