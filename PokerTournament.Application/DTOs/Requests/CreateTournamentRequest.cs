using System.Text.Json.Serialization;
using PokerTournament.Application.Publicity;

namespace PokerTournament.Application.DTOs.Requests;

public class CreateTournamentRequest : ITournamentPublicityInput
{
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    // SPEC-20261103-1927 (revisão): ranking, staff e PIX do dia guardam se vieram no corpo (ver Sent). Na edição, ausente =
    // manter o gravado; null explícito continua apagando
    public Guid? RankingId { get => _rankingId; set { _rankingId = value; Sent.Add(nameof(RankingId)); } }
    public Guid? BlindStructureId { get; set; }
    public decimal BuyInAmount { get; set; }
    public decimal? RebuyAmount { get; set; }
    public decimal? AddonAmount { get; set; }
    public int StartingStack { get; set; }
    public int? RebuyStack { get; set; }
    public int? AddonStack { get; set; }
    public int? MaxRebuys { get; set; }
    public bool AddonAllowed { get; set; }
    // SPEC-20261027-1311: máximo de add-ons por jogador (0 = ilimitado). Na criação, null = herdar da estrutura (sem
    // estrutura: 1); na edição, null = manter. Negativo → 400.
    public int? MaxAddons { get; set; }
    public int? LateRegistrationLevel { get; set; }
    public int? RebuyUntilLevel { get; set; }
    public int SeatsPerTable { get; set; } = 9;
    public string? ResponsiblePixKey { get => _responsiblePixKey; set { _responsiblePixKey = value; Sent.Add(nameof(ResponsiblePixKey)); } }
    // SPEC-20261023-1218: tipo escolhido da chave do dia (null = detectar); a chave é normalizada no TournamentService
    public string? ResponsiblePixKeyType { get; set; }
    public decimal? StaffAmount { get => _staffAmount; set { _staffAmount = value; Sent.Add(nameof(StaffAmount)); } }
    public string? RankingContribMode { get => _rankingContribMode; set { _rankingContribMode = value; Sent.Add(nameof(RankingContribMode)); } }
    public decimal? RankingContribValue { get => _rankingContribValue; set { _rankingContribValue = value; Sent.Add(nameof(RankingContribValue)); } }

    // SPEC-20261023-1215 (CP17): quantos dealers trabalham no torneio quando a estrutura tem dealer pago.
    // null = 1 (cliente que não conhece o campo); 0 = sem custo de dealer; fora de 0–20 → 400. Não é gravado
    // no torneio: vira o custo "Dealers (N × R$ X)" na criação (e na primeira estrutura de um rascunho).
    public int? DealerCount { get; set; }

    // SPEC-20261026-1909 — formato knockout: "None" (Normal), "Simple" ou "Progressive" ("Normal" também vale None).
    // Na criação, null = herdar o padrão da estrutura escolhida (sem estrutura: Normal); na edição, null = manter.
    // BountyAmount: parte fixa do buy-in (e de cada rebuy) que vira bounty; null no knockout = herdar/manter.
    public string? KnockoutType { get; set; }
    public decimal? BountyAmount { get; set; }

    // SPEC-20261029-1722 — timebank virtual. TimebankCount: 0 = sem timebank; 1 a 99 = timebanks por jogador. Na criação,
    // null = herdar o padrão da estrutura escolhida (sem estrutura ou estrutura sem timebank: desligado); na edição, null =
    // manter. TimebankSeconds: 5 a 300 (null = o da estrutura/o atual, senão 30); ignorado com o timebank desligado.
    // Mudar a quantidade no rascunho reaplica nas inscrições que já existem.
    public int? TimebankCount { get; set; }
    public int? TimebankSeconds { get; set; }

    // SPEC-20261108-1639 — divulgação (torneio público). Os quatro guardam se vieram no corpo (Sent): na edição, ausente =
    // manter o gravado (um cliente antigo não despublica nada); na criação, ausente = privado, sem descrição/vagas/WhatsApp.
    // IsPublic liga a página /t/{slug} e o "perto de mim" (o slug nasce na primeira vez e não muda). PublicDescription até
    // 1.000 caracteres (vazio → null). MaxPlayers: null/0 = sem limite de vagas; 2 a 999. ShowOrganizerWhatsapp: a porta do
    // torneio para o WhatsApp do organizador na página pública (a conta dele também precisa ter o opt-in).
    public bool? IsPublic { get => _isPublic; set { _isPublic = value; Sent.Add(nameof(IsPublic)); } }
    public string? PublicDescription { get => _publicDescription; set { _publicDescription = value; Sent.Add(nameof(PublicDescription)); } }
    public int? MaxPlayers { get => _maxPlayers; set { _maxPlayers = value; Sent.Add(nameof(MaxPlayers)); } }
    public bool? ShowOrganizerWhatsapp { get => _showOrganizerWhatsapp; set { _showOrganizerWhatsapp = value; Sent.Add(nameof(ShowOrganizerWhatsapp)); } }

    /// <summary>
    /// Quais de RankingId, RankingContribMode, RankingContribValue, StaffAmount e ResponsiblePixKey vieram no corpo (com valor
    /// ou null). O System.Text.Json só chama o setter do que está no JSON, então o campo ausente não entra aqui
    /// (SPEC-20261103-1927, revisão: a tela que ainda não sabia o plano mandava esses campos vazios e a edição de um torneio
    /// em andamento tirava o staff e o ranking de todos e apagava o PIX do dia). Só a edição usa; a criação segue com ausente = nulo.
    /// </summary>
    [JsonIgnore]
    public HashSet<string> Sent { get; } = [];

    /// <summary>O campo veio no corpo (divulgação — SPEC-20261108-1639).</summary>
    public bool Has(string field) => Sent.Contains(field);

    private Guid? _rankingId;
    private string? _responsiblePixKey;
    private decimal? _staffAmount;
    private string? _rankingContribMode;
    private decimal? _rankingContribValue;
    private bool? _isPublic;
    private string? _publicDescription;
    private int? _maxPlayers;
    private bool? _showOrganizerWhatsapp;
}
