using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

/// <summary>Tabela <c>notifications</c> (SPEC-20261108-1635): o sino e o e-mail resumo leem por conta, não lidas primeiro.</summary>
public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at");
        builder.Property(n => n.UpdatedAt).HasColumnName("updated_at");
        builder.Property(n => n.xmin)
            .IsRowVersion();

        builder.Property(n => n.UserId).HasColumnName("user_id");

        builder.Property(n => n.Kind)
            .HasColumnName("kind")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(n => n.Title)
            .HasColumnName("title")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(n => n.Body)
            .HasColumnName("body")
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(n => n.Link)
            .HasColumnName("link")
            .HasMaxLength(500);

        builder.Property(n => n.ReadAt).HasColumnName("read_at");
        builder.Property(n => n.EmailedAt).HasColumnName("emailed_at");

        builder.HasOne(n => n.User)
            .WithMany(u => u.Notifications)
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // O sino lista as 20 últimas da conta; o resumo e a limpeza filtram por read_at
        builder.HasIndex(n => new { n.UserId, n.CreatedAt }).HasDatabaseName("ix_notifications_user_id_created_at");
        builder.HasIndex(n => new { n.UserId, n.ReadAt }).HasDatabaseName("ix_notifications_user_id_read_at");
    }
}
