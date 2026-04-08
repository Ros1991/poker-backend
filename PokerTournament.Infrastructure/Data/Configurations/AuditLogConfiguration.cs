using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.EntityType)
            .HasColumnName("entity_type")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.EntityId).HasColumnName("entity_id");

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(a => a.OldValues).HasColumnName("old_values").HasColumnType("jsonb");
        builder.Property(a => a.NewValues).HasColumnName("new_values").HasColumnType("jsonb");
        builder.Property(a => a.ChangedFields).HasColumnName("changed_fields").HasColumnType("jsonb");
        builder.Property(a => a.UserId).HasColumnName("user_id");

        builder.Property(a => a.UserName)
            .HasColumnName("user_name")
            .HasMaxLength(200);

        builder.Property(a => a.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(50);

        builder.Property(a => a.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(500);

        builder.Property(a => a.TournamentId).HasColumnName("tournament_id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        // Indexes
        builder.HasIndex(a => new { a.EntityType, a.EntityId })
            .HasDatabaseName("ix_audit_logs_entity_type_entity_id");

        builder.HasIndex(a => a.TournamentId)
            .HasDatabaseName("ix_audit_logs_tournament_id");

        builder.HasIndex(a => a.CreatedAt)
            .HasDatabaseName("ix_audit_logs_created_at");

        builder.HasIndex(a => a.UserId)
            .HasDatabaseName("ix_audit_logs_user_id");
    }
}
