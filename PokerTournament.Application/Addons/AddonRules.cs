using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Addons;

/// <summary>
/// Regras do add-on (SPEC-20261027-1311) num lugar só — EntryService, TournamentService, BlindStructuresController e
/// TournamentHistoryService usam daqui.
/// <list type="bullet">
/// <item>Cada add-on é uma unidade (valor e fichas do add-on por unidade). O jogador compra até o máximo do torneio
/// (<see cref="Tournament.MaxAddons"/>; 0 = ilimitado), um ou mais por vez (1 a <see cref="MaxPerRequest"/>, como o rebuy).</item>
/// <item>O antigo "addon duplo" deixou de existir: a migration AddonMaximo transformou a inscrição com duplo em 2 add-ons
/// e o torneio/estrutura com duplo permitido em máximo 2. O lançamento antigo "Addon Duplo - …" vale 2 unidades.</item>
/// </list>
/// </summary>
public static class AddonRules
{
    /// <summary>Máximo padrão de add-ons por jogador (torneio sem estrutura, estrutura nova)</summary>
    public const int DefaultMax = 1;

    /// <summary>Maior "Max add-ons" aceito (0 = ilimitado)</summary>
    public const int MaxLimit = 99;

    /// <summary>Máximo de add-ons num pedido só (painel e token de dealer chamam o mesmo método)</summary>
    public const int MaxPerRequest = 10;

    public const string InvalidQuantityMessage = "Quantidade de add-ons inválida.";

    public const string InvalidMaxMessage = "Máximo de add-ons inválido: use de 0 (ilimitado) a 99.";

    public static string LimitReachedMessage(int max) => $"Limite de add-ons atingido (máximo {max}).";

    /// <summary>Confere o "Max add-ons" informado (0 = ilimitado, até <see cref="MaxLimit"/>); null = usar o <paramref name="fallback"/>.</summary>
    public static int Resolve(int? requested, int fallback)
    {
        var value = requested ?? fallback;
        if (value < 0 || value > MaxLimit)
            throw new DomainException(InvalidMaxMessage);
        return value;
    }

    /// <summary>Quantos add-ons o jogador ainda pode comprar; null = sem limite</summary>
    public static int? Remaining(Tournament tournament, TournamentEntry entry) =>
        tournament.MaxAddons > 0 ? Math.Max(0, tournament.MaxAddons - entry.AddonCount) : null;

    /// <summary>
    /// Quantas unidades um lançamento de add-on representa: o antigo "Addon Duplo - …" (gravado antes da SPEC-20261027-1311)
    /// vale 2; qualquer outro, 1 (desde então cada add-on é um lançamento).
    /// </summary>
    public static int Units(Transaction tx) =>
        IsLegacyDouble(tx) ? 2 : 1;

    public static bool IsLegacyDouble(Transaction tx) =>
        tx.Description?.StartsWith("Addon Duplo", StringComparison.OrdinalIgnoreCase) == true;
}
