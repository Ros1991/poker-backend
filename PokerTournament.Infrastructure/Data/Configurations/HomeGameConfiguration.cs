using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Application.Common;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class HomeGameConfiguration : IEntityTypeConfiguration<HomeGame>
{
    public void Configure(EntityTypeBuilder<HomeGame> builder)
    {
        builder.ToTable("home_games");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnName("id");
        builder.Property(h => h.CreatedAt).HasColumnName("created_at");
        builder.Property(h => h.UpdatedAt).HasColumnName("updated_at");
        builder.Property(h => h.DeletedAt).HasColumnName("deleted_at");
        builder.Property(h => h.xmin)
            .IsRowVersion();

        builder.Property(h => h.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(h => h.Description).HasColumnName("description");
        builder.Property(h => h.Location).HasColumnName("location");
        builder.Property(h => h.LogoUrl).HasColumnName("logo_url");
        builder.Property(h => h.PixKey).HasColumnName("pix_key");
        builder.Property(h => h.PixBeneficiary).HasColumnName("pix_beneficiary");

        builder.Property(h => h.Timezone)
            .HasColumnName("timezone")
            .HasMaxLength(100)
            .HasDefaultValue("America/Sao_Paulo");

        builder.Property(h => h.DefaultBuyIn)
            .HasColumnName("default_buy_in")
            .HasColumnType("decimal(10,2)");

        builder.Property(h => h.DefaultRebuy)
            .HasColumnName("default_rebuy")
            .HasColumnType("decimal(10,2)");

        builder.Property(h => h.DefaultAddon)
            .HasColumnName("default_addon")
            .HasColumnType("decimal(10,2)");

        builder.Property(h => h.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(h => h.OwnerId)
            .HasColumnName("owner_id")
            .IsRequired();

        // Principal da conta (SPEC-20261029-1723); sem valor padrão no modelo (o EF grava sempre o que a entidade tem)
        builder.Property(h => h.IsPrimary)
            .HasColumnName("is_primary")
            .IsRequired();

        // Endereço estruturado e coordenadas (SPEC-20261108-1634)
        builder.Property(h => h.PostalCode).HasColumnName("postal_code").HasMaxLength(8);
        builder.Property(h => h.Street).HasColumnName("street").HasMaxLength(200);
        builder.Property(h => h.Number).HasColumnName("address_number").HasMaxLength(20);
        builder.Property(h => h.Complement).HasColumnName("complement").HasMaxLength(100);
        builder.Property(h => h.District).HasColumnName("district").HasMaxLength(100);
        builder.Property(h => h.City).HasColumnName("city").HasMaxLength(100);
        builder.Property(h => h.State).HasColumnName("state").HasMaxLength(2);
        builder.Property(h => h.Latitude).HasColumnName("latitude");
        builder.Property(h => h.Longitude).HasColumnName("longitude");
        builder.Property(h => h.GeocodedAt).HasColumnName("geocoded_at");
        builder.Property(h => h.GeocodePrecision).HasColumnName("geocode_precision").HasMaxLength(20);
        builder.Property(h => h.GeocodeFailedAt).HasColumnName("geocode_failed_at");
        builder.Property(h => h.AddressVisibility)
            .HasColumnName("address_visibility")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(nameof(Domain.Enums.AddressVisibility.ApprovedOnly));

        // Busca por raio (GeoDistance.Nearby): o filtro por caixa de latitude/longitude usa este índice; só quem tem
        // coordenadas entra
        builder.HasIndex(h => new { h.Latitude, h.Longitude })
            .HasFilter("latitude IS NOT NULL AND longitude IS NOT NULL")
            .HasDatabaseName("ix_home_games_lat_lng");

        // Nome único POR DONO entre os não excluídos (revisão geral do programa, ondas 7–9): donos diferentes podem
        // repetir o nome; o mesmo dono recebe 400 "Você já tem um home game chamado X." (HomeGamesController, também na
        // corrida). O índice antigo ix_home_games_name era do banco inteiro e o 2º grupo com o mesmo nome levava 500.
        builder.HasIndex(h => new { h.OwnerId, h.Name })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(HomeGameNames.UniqueOwnerNameIndex);

        // Índice da FK do dono declarado à parte: o composto acima é parcial (deleted_at IS NULL) e não serve às
        // buscas por dono que incluem os excluídos
        builder.HasIndex(h => h.OwnerId);

        // Um principal por dono entre os não excluídos (SPEC-20261029-1723): dois caminhos criando ao mesmo tempo
        // não deixam a conta com dois
        builder.HasIndex(h => h.OwnerId, HomeGamePrimary.UniqueIndex)
            .IsUnique()
            .HasFilter("is_primary AND deleted_at IS NULL");

        builder.HasIndex(h => h.DeletedAt)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_home_games_not_deleted");

        // Relations
        builder.HasOne(h => h.Owner)
            .WithMany(u => u.OwnedHomeGames)
            .HasForeignKey(h => h.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(h => h.Members)
            .WithOne(m => m.HomeGame)
            .HasForeignKey(m => m.HomeGameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(h => h.Rankings)
            .WithOne(r => r.HomeGame)
            .HasForeignKey(r => r.HomeGameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(h => h.Tournaments)
            .WithOne(t => t.HomeGame)
            .HasForeignKey(t => t.HomeGameId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
