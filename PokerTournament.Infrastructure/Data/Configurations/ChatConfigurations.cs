using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

/// <summary>
/// Tabela <c>conversations</c> (SPEC-20261108-1638): par ordenado de contas, uma conversa <c>Direct</c> por par e uma
/// <c>TournamentRequest</c> por par e torneio — dois índices únicos PARCIAIS (um índice só com tournament_id nulo não
/// garantiria a unicidade: NULL é distinto em índice único do PostgreSQL). Conta excluída de verdade leva a conversa junto.
/// </summary>
public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.xmin).IsRowVersion();

        builder.Property(c => c.Type).HasColumnName("type").IsRequired().HasMaxLength(30);
        builder.Property(c => c.TournamentId).HasColumnName("tournament_id");
        builder.Property(c => c.UserLowId).HasColumnName("user_low_id");
        builder.Property(c => c.UserHighId).HasColumnName("user_high_id");
        builder.Property(c => c.CreatedById).HasColumnName("created_by_id");
        builder.Property(c => c.LastMessageAt).HasColumnName("last_message_at");

        builder.HasOne(c => c.UserLow)
            .WithMany()
            .HasForeignKey(c => c.UserLowId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.UserHigh)
            .WithMany()
            .HasForeignKey(c => c.UserHighId)
            .OnDelete(DeleteBehavior.Cascade);
        // Torneio excluído de verdade: a conversa fica (sem o torneio)
        builder.HasOne(c => c.Tournament)
            .WithMany()
            .HasForeignKey(c => c.TournamentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => new { c.UserLowId, c.UserHighId })
            .IsUnique()
            .HasFilter("type = 'Direct'")
            .HasDatabaseName("ux_conversations_direct_pair");
        builder.HasIndex(c => new { c.UserLowId, c.UserHighId, c.TournamentId })
            .IsUnique()
            .HasFilter("type = 'TournamentRequest'")
            .HasDatabaseName("ux_conversations_request_pair");
        // Lista por conta (a conta pode estar em qualquer lado do par) e o limite de 20 conversas novas por dia
        builder.HasIndex(c => new { c.UserHighId, c.LastMessageAt }).HasDatabaseName("ix_conversations_user_high_id_last_message_at");
        builder.HasIndex(c => new { c.CreatedById, c.CreatedAt }).HasDatabaseName("ix_conversations_created_by_id_created_at");
    }
}

/// <summary>Tabela <c>conversation_members</c> (SPEC-20261108-1638): uma linha por conta e conversa, com "lida até".</summary>
public class ConversationMemberConfiguration : IEntityTypeConfiguration<ConversationMember>
{
    public void Configure(EntityTypeBuilder<ConversationMember> builder)
    {
        builder.ToTable("conversation_members");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        builder.Property(m => m.xmin).IsRowVersion();

        builder.Property(m => m.ConversationId).HasColumnName("conversation_id");
        builder.Property(m => m.UserId).HasColumnName("user_id");
        builder.Property(m => m.LastReadAt).HasColumnName("last_read_at");

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Members)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.ConversationId, m.UserId })
            .IsUnique()
            .HasDatabaseName("ux_conversation_members_conversation_id_user_id");
        builder.HasIndex(m => m.UserId).HasDatabaseName("ix_conversation_members_user_id");
    }
}

/// <summary>
/// Tabela <c>messages</c> (SPEC-20261108-1638). <c>deleted_at</c> é coluna própria (não é soft delete do contexto): a
/// mensagem apagada continua na lista como "mensagem apagada". Índice por conversa e data (histórico paginado) e por
/// remetente e data (limite de 60 por hora).
/// </summary>
public class MessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        builder.Property(m => m.xmin).IsRowVersion();

        builder.Property(m => m.ConversationId).HasColumnName("conversation_id");
        builder.Property(m => m.SenderId).HasColumnName("sender_id");
        builder.Property(m => m.Body).HasColumnName("body").IsRequired().HasMaxLength(ChatMessage.BodyMaxLength);
        builder.Property(m => m.SentAt).HasColumnName("sent_at");
        builder.Property(m => m.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.ConversationId, m.SentAt }).HasDatabaseName("ix_messages_conversation_id_sent_at");
        builder.HasIndex(m => new { m.SenderId, m.SentAt }).HasDatabaseName("ix_messages_sender_id_sent_at");
    }
}

/// <summary>
/// Tabela <c>reports</c> (SPEC-20261108-1638). A denúncia fica mesmo que a mensagem ou a conversa sumam (SetNull); conta
/// excluída de verdade leva as denúncias em que aparece. A cópia do texto denunciado vive aqui.
/// </summary>
public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("reports");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.xmin).IsRowVersion();

        builder.Property(r => r.ReporterId).HasColumnName("reporter_id");
        builder.Property(r => r.ReportedUserId).HasColumnName("reported_user_id");
        builder.Property(r => r.ConversationId).HasColumnName("conversation_id");
        builder.Property(r => r.MessageId).HasColumnName("message_id");
        builder.Property(r => r.Reason).HasColumnName("reason").IsRequired().HasMaxLength(30);
        builder.Property(r => r.Details).HasColumnName("details").HasMaxLength(Report.DetailsMaxLength);
        builder.Property(r => r.ReportedText).HasColumnName("reported_text").HasMaxLength(Report.ReportedTextMaxLength);
        builder.Property(r => r.Status).HasColumnName("status").IsRequired().HasMaxLength(20);
        builder.Property(r => r.ReviewedById).HasColumnName("reviewed_by_id");
        builder.Property(r => r.ReviewedAt).HasColumnName("reviewed_at");

        builder.HasOne(r => r.Reporter)
            .WithMany()
            .HasForeignKey(r => r.ReporterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.ReportedUser)
            .WithMany()
            .HasForeignKey(r => r.ReportedUserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.Conversation)
            .WithMany()
            .HasForeignKey(r => r.ConversationId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(r => r.Message)
            .WithMany()
            .HasForeignKey(r => r.MessageId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(r => r.ReviewedBy)
            .WithMany()
            .HasForeignKey(r => r.ReviewedById)
            .OnDelete(DeleteBehavior.SetNull);

        // A Administração lista por situação, mais recente primeiro
        builder.HasIndex(r => new { r.Status, r.CreatedAt }).HasDatabaseName("ix_reports_status_created_at");
        builder.HasIndex(r => r.ReportedUserId).HasDatabaseName("ix_reports_reported_user_id");
    }
}
