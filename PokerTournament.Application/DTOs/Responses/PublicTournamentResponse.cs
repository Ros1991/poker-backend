namespace PokerTournament.Application.DTOs.Responses;

/// <summary>
/// Página pública do torneio, <c>GET public/tournaments/{slug}</c> (SPEC-20261108-1639, anônima). Só o que um estranho
/// pode ver: nada de rua/número/CEP (D6), coordenadas, e-mail, WhatsApp de jogador nem dinheiro de terceiros. A
/// distância vem só quando o visitante manda <c>?lat&amp;lng</c> (nunca guardados).
/// </summary>
public class PublicTournamentResponse
{
    public Guid TournamentId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Texto do organizador para quem é de fora (PublicDescription).</summary>
    public string? Description { get; set; }
    public DateTimeOffset Date { get; set; }
    /// <summary>Draft | OpenForRegistration | InProgress | BreakSettlement | Finished.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Já começou (em andamento ou intervalo de acerto).</summary>
    public bool HasStarted { get; set; }
    /// <summary>Já aconteceu (finalizado): a página avisa e não pede vaga.</summary>
    public bool IsOver { get; set; }

    // O clube
    public string ClubName { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    /// <summary>"Bairro, Cidade - UF" (ou o que houver); null sem endereço.</summary>
    public string? LocationSummary { get; set; }
    /// <summary>Distância do visitante ao clube (km), só com ?lat&amp;lng e clube geocodificado.</summary>
    public double? DistanceKm { get; set; }

    // Valores e regras
    public decimal BuyInAmount { get; set; }
    public decimal? RebuyAmount { get; set; }
    public decimal? AddonAmount { get; set; }
    public int? MaxRebuys { get; set; }
    public bool AddonAllowed { get; set; }
    /// <summary>0 = ilimitado.</summary>
    public int MaxAddons { get; set; }
    public int StartingStack { get; set; }
    public int? RebuyStack { get; set; }
    public int? AddonStack { get; set; }
    /// <summary>None | Simple | Progressive.</summary>
    public string KnockoutType { get; set; } = "None";
    public decimal? BountyAmount { get; set; }
    public int? TimebankCount { get; set; }
    public int? TimebankSeconds { get; set; }

    // Estrutura resumida
    public string? StructureName { get; set; }
    public int LevelCount { get; set; }
    public PublicBlindLevel? FirstLevel { get; set; }
    /// <summary>Duração do nível mais comum (minutos); null sem níveis.</summary>
    public int? TypicalLevelMinutes { get; set; }

    // Vagas e premiação prevista
    public int? MaxPlayers { get; set; }
    /// <summary>Inscritos + reservas aprovadas (quem já tem lugar garantido).</summary>
    public int ConfirmedPlayers { get; set; }
    /// <summary>Vagas que sobram (null sem limite).</summary>
    public int? SpotsLeft { get; set; }
    /// <summary>Premiação prevista com quem já confirmou (buy-in × confirmados, ou o líquido atual se for maior).</summary>
    public decimal EstimatedPrizePool { get; set; }
    /// <summary>Premiação com a casa cheia (buy-in × vagas), só com vagas definidas.</summary>
    public decimal? FullHousePrizePool { get; set; }

    // Quem organiza (quem criou o torneio, senão o dono do clube): nome público e foto; o WhatsApp só com as DUAS portas
    // (ShowOrganizerWhatsapp do torneio e ShowWhatsappToPlayers da conta)
    public string OrganizerName { get; set; } = string.Empty;
    public string? OrganizerPhotoUrl { get; set; }
    /// <summary>Só dígitos com DDI, ou null.</summary>
    public string? OrganizerWhatsapp { get; set; }

    // Marca do rodapé, pela regra da página pública do ranking (plano do dono)
    public string Plan { get; set; } = "Mesa";
    public bool ShowBadge { get; set; } = true;
    public string? ClubLogoUrl { get; set; }
}

public class PublicBlindLevel
{
    public int SmallBlind { get; set; }
    public int BigBlind { get; set; }
    public int Ante { get; set; }
    public int DurationMinutes { get; set; }
}
