using System.Text.Json.Serialization;
using PokerTournament.Application.Publicity;

namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// <c>PUT tournaments/{id}/publicity</c> (SPEC-20261108-1639): o atalho "Divulgar" do painel mexe só na divulgação —
/// público, descrição, vagas e o WhatsApp do organizador. Campo ausente no corpo = manter (System.Text.Json só chama o
/// setter do que está no JSON); null explícito limpa a descrição/as vagas.
/// </summary>
public class UpdateTournamentPublicityRequest : ITournamentPublicityInput
{
    public bool? IsPublic { get => _isPublic; set { _isPublic = value; Sent.Add(nameof(IsPublic)); } }
    public string? PublicDescription { get => _publicDescription; set { _publicDescription = value; Sent.Add(nameof(PublicDescription)); } }
    public int? MaxPlayers { get => _maxPlayers; set { _maxPlayers = value; Sent.Add(nameof(MaxPlayers)); } }
    public bool? ShowOrganizerWhatsapp { get => _showOrganizerWhatsapp; set { _showOrganizerWhatsapp = value; Sent.Add(nameof(ShowOrganizerWhatsapp)); } }

    [JsonIgnore]
    public HashSet<string> Sent { get; } = [];

    public bool Has(string field) => Sent.Contains(field);

    private bool? _isPublic;
    private string? _publicDescription;
    private int? _maxPlayers;
    private bool? _showOrganizerWhatsapp;
}
