using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Publicity;

/// <summary>O que a tela de divulgação manda (formulário do torneio e atalho "Divulgar" do painel): null = não mexer.</summary>
public interface ITournamentPublicityInput
{
    bool? IsPublic { get; }
    string? PublicDescription { get; }
    int? MaxPlayers { get; }
    bool? ShowOrganizerWhatsapp { get; }
    /// <summary>Quais campos vieram no corpo (ausente = manter o gravado).</summary>
    bool Has(string field);
}

/// <summary>
/// Regras da divulgação do torneio (SPEC-20261108-1639) num lugar só: validação e cópia dos quatro campos para a entidade
/// (<see cref="Apply"/>) e o slug público (<see cref="EnsureSlugAsync"/>), gerado UMA vez — clube + torneio + data,
/// com sufixo <c>-2</c>, <c>-3</c>… em colisão (contando os torneios excluídos: um link antigo nunca abre outro torneio)
/// — e nunca mais mudado, mesmo que o nome mude ou o torneio volte a ser privado.
/// </summary>
public static class TournamentPublicity
{
    public const int DescriptionMaxLength = 1000;
    public const int MinPlayers = 2;
    public const int MaxPlayersLimit = 999;
    public const int SlugMaxLength = 80;

    public const string DescriptionTooLongMessage = "A descrição pública pode ter no máximo 1.000 caracteres.";
    public const string InvalidMaxPlayersMessage = "Vagas inválidas: use de 2 a 999, ou deixe em branco para não limitar.";

    /// <summary>
    /// Copia os campos que vieram (<see cref="ITournamentPublicityInput.Has"/>) para o torneio, normalizados e validados
    /// (<see cref="DomainException"/> → 400). Devolve se o torneio ACABOU de ficar público (quem chama gera o slug).
    /// </summary>
    public static bool Apply(Tournament tournament, ITournamentPublicityInput input)
    {
        var wasPublic = tournament.IsPublic;
        if (input.Has(nameof(input.IsPublic)))
            tournament.IsPublic = input.IsPublic ?? false;
        if (input.Has(nameof(input.PublicDescription)))
        {
            var text = input.PublicDescription?.Trim();
            if (string.IsNullOrEmpty(text))
                text = null;
            else if (text.Length > DescriptionMaxLength)
                throw new DomainException(DescriptionTooLongMessage);
            tournament.PublicDescription = text;
        }
        if (input.Has(nameof(input.MaxPlayers)))
        {
            var max = input.MaxPlayers is null or 0 ? null : input.MaxPlayers;
            if (max is { } m && (m < MinPlayers || m > MaxPlayersLimit))
                throw new DomainException(InvalidMaxPlayersMessage);
            tournament.MaxPlayers = max;
        }
        if (input.Has(nameof(input.ShowOrganizerWhatsapp)))
            tournament.ShowOrganizerWhatsapp = input.ShowOrganizerWhatsapp ?? false;
        return tournament.IsPublic && !wasPublic;
    }

    /// <summary>
    /// Garante o slug de um torneio público (idempotente: quem já tem, mantém). Base = clube + nome + data
    /// (<c>clube-do-ze-sexta-poker-2026-11-21</c>), única entre todos os torneios, inclusive os excluídos.
    /// </summary>
    public static async Task EnsureSlugAsync(IAppDbContext db, Tournament tournament, string clubName, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(tournament.PublicSlug))
            return;
        var date = tournament.Date.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var baseSlug = SlugGenerator.FromName($"{clubName} {tournament.Name} {date}");
        if (baseSlug == "ranking")
            baseSlug = $"torneio-{date}";
        var taken = (await db.Tournaments
                .IgnoreQueryFilters()
                .Where(t => t.PublicSlug != null)
                .Select(t => t.PublicSlug!)
                .ToListAsync(ct))
            .ToHashSet();
        tournament.PublicSlug = SlugGenerator.MakeUnique(baseSlug, taken);
    }

    /// <summary>Caminho da página pública no site (o link que o organizador copia e compartilha).</summary>
    public static string PublicPath(string slug) => $"/t/{slug}";
}
