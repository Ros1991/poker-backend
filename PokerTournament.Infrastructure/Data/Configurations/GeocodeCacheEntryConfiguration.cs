using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

/// <summary>Cache das consultas ao geocodificador (SPEC-20261108-1634): tabela <c>geocode_cache</c>.</summary>
public class GeocodeCacheEntryConfiguration : IEntityTypeConfiguration<GeocodeCacheEntry>
{
    public void Configure(EntityTypeBuilder<GeocodeCacheEntry> builder)
    {
        builder.ToTable("geocode_cache");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id");
        builder.Property(g => g.CreatedAt).HasColumnName("created_at");
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at");
        builder.Property(g => g.xmin).IsRowVersion();

        builder.Property(g => g.QueryKey)
            .HasColumnName("query_key")
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(g => g.Found).HasColumnName("found").IsRequired();
        builder.Property(g => g.Latitude).HasColumnName("latitude");
        builder.Property(g => g.Longitude).HasColumnName("longitude");

        builder.Property(g => g.Provider)
            .HasColumnName("provider")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(g => g.ResolvedName)
            .HasColumnName("resolved_name")
            .HasMaxLength(500);

        // Uma linha por consulta normalizada (a corrida de dois trabalhos com a mesma consulta cai aqui e é tratada)
        builder.HasIndex(g => g.QueryKey)
            .IsUnique()
            .HasDatabaseName("ix_geocode_cache_query_key");
    }
}
