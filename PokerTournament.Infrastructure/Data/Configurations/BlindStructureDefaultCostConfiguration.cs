using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

/// <summary>
/// Custos padrão da estrutura de blinds (SPEC-20261023-1215, CP1): filhos de blind_structures,
/// apagados em cascata junto com a estrutura. Copiados para cost_extras na criação do torneio.
/// </summary>
public class BlindStructureDefaultCostConfiguration : IEntityTypeConfiguration<BlindStructureDefaultCost>
{
    public void Configure(EntityTypeBuilder<BlindStructureDefaultCost> builder)
    {
        builder.ToTable("blind_structure_default_costs");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.xmin)
            .IsRowVersion();

        builder.Property(c => c.BlindStructureId).HasColumnName("blind_structure_id");

        builder.Property(c => c.Description)
            .HasColumnName("description")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.Amount)
            .HasColumnName("amount")
            .HasColumnType("decimal(10,2)");

        builder.Property(c => c.Beneficiary).HasColumnName("beneficiary").HasMaxLength(200);
        builder.Property(c => c.PixKey).HasColumnName("pix_key").HasMaxLength(200);
        builder.Property(c => c.PixKeyType).HasColumnName("pix_key_type").HasMaxLength(50);
        builder.Property(c => c.SortOrder).HasColumnName("sort_order");

        builder.HasOne(c => c.BlindStructure)
            .WithMany(b => b.DefaultCosts)
            .HasForeignKey(c => c.BlindStructureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.BlindStructureId)
            .HasDatabaseName("ix_blind_structure_default_costs_structure");
    }
}
