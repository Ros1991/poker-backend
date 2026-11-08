namespace PokerTournament.Domain.Entities;

public class HomeGame : SoftDeletableEntity
{
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? LogoUrl { get; set; }
    public string? PixKey { get; set; }
    public string? PixBeneficiary { get; set; }
    public string Timezone { get; set; } = "America/Sao_Paulo";
    public decimal? DefaultBuyIn { get; set; }
    public decimal? DefaultRebuy { get; set; }
    public decimal? DefaultAddon { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Home game principal da conta (SPEC-20261029-1723): um por dono, criado junto da conta (ou pela migration
    /// HomeGamePrincipal) e nunca excluído. No Mesa e no Clube é o único — a interface não fala em "home game": a conta
    /// é o clube, e no Clube o <see cref="Name"/> é o "nome do clube". Só a Liga cria outros (até 3 com o principal).
    /// </summary>
    public bool IsPrimary { get; set; }

    // Endereço estruturado e coordenadas (SPEC-20261108-1634). O CEP vem do ViaCEP pelo servidor (rua, bairro, cidade e
    // UF preenchidos; podem ser corrigidos à mão); número e complemento são do organizador. As coordenadas são
    // geocodificadas em segundo plano ao salvar o endereço (Nominatim, fila de 1 pedido/s, cache em geocode_cache) e
    // zeradas quando o endereço muda. Location (texto livre) continua como ponto de referência opcional.
    /// <summary>CEP só com os 8 dígitos.</summary>
    public string? PostalCode { get; set; }
    public string? Street { get; set; }
    /// <summary>Número do endereço (texto: "123", "s/n", "1578 A").</summary>
    public string? Number { get; set; }
    public string? Complement { get; set; }
    /// <summary>Bairro.</summary>
    public string? District { get; set; }
    public string? City { get; set; }
    /// <summary>UF com 2 letras maiúsculas.</summary>
    public string? State { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>Quando as coordenadas foram achadas (null = ainda não, ou o endereço mudou).</summary>
    public DateTimeOffset? GeocodedAt { get; set; }
    /// <summary>Nível que respondeu (<see cref="Enums.GeocodePrecision"/> via nameof); null sem coordenadas.</summary>
    public string? GeocodePrecision { get; set; }
    /// <summary>Última vez em que a geocodificação desistiu (não achou em nenhum nível ou esgotou as tentativas); null enquanto não falhou.</summary>
    public DateTimeOffset? GeocodeFailedAt { get; set; }
    /// <summary>Quem vê o endereço completo (<see cref="Enums.AddressVisibility"/> via nameof; padrão ApprovedOnly).</summary>
    public string AddressVisibility { get; set; } = nameof(Enums.AddressVisibility.ApprovedOnly);

    // Navigation
    public User Owner { get; set; } = null!;
    public ICollection<HomeGameMember> Members { get; set; } = [];
    public ICollection<Person> Persons { get; set; } = [];
    public ICollection<Ranking> Rankings { get; set; } = [];
    public ICollection<Tournament> Tournaments { get; set; } = [];
}
