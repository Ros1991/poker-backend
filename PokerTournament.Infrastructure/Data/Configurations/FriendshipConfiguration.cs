using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

/// <summary>
/// Tabela <c>friendships</c> (SPEC-20261108-1637): um registro por par ordenado de contas (índice único em
/// user_low_id + user_high_id). Conta excluída de verdade leva o registro junto (cascade).
/// </summary>
public class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("friendships");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");
        builder.Property(f => f.xmin).IsRowVersion();

        builder.Property(f => f.UserLowId).HasColumnName("user_low_id");
        builder.Property(f => f.UserHighId).HasColumnName("user_high_id");
        builder.Property(f => f.RequesterId).HasColumnName("requester_id");
        builder.Property(f => f.Status).HasColumnName("status").IsRequired().HasMaxLength(20);
        builder.Property(f => f.RequestedAt).HasColumnName("requested_at");
        builder.Property(f => f.RespondedAt).HasColumnName("responded_at");
        builder.Property(f => f.BlockedById).HasColumnName("blocked_by_id");

        builder.HasOne(f => f.UserLow)
            .WithMany()
            .HasForeignKey(f => f.UserLowId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(f => f.UserHigh)
            .WithMany()
            .HasForeignKey(f => f.UserHighId)
            .OnDelete(DeleteBehavior.Cascade);

        // Um registro por par: dois pedidos simultâneos (ou cruzados) não viram duas linhas
        builder.HasIndex(f => new { f.UserLowId, f.UserHighId })
            .IsUnique()
            .HasDatabaseName("ux_friendships_pair");
        // Listas por conta (amigos, pedidos, bloqueios): a conta pode estar em qualquer lado do par
        builder.HasIndex(f => new { f.UserHighId, f.Status }).HasDatabaseName("ix_friendships_user_high_id_status");
        // Pedidos enviados pendentes (limite de 20) e "quem bloqueou"
        builder.HasIndex(f => new { f.RequesterId, f.Status }).HasDatabaseName("ix_friendships_requester_id_status");
    }
}
