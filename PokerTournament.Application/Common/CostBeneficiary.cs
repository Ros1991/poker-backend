using System.Globalization;
using System.Text;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Common;

/// <summary>
/// Quem é o favorecido de um custo (SPEC-20261103-1927). O custo não guarda a pessoa, só o nome ("Para quem" da aba Custos;
/// no custo de staff, o beneficiário do PIX do home game). O jogador é o favorecido quando esse nome é igual ao nome
/// completo ou ao apelido dele, sem diferença de acento, maiúsculas e espaços. Aí o pagamento com destino nesse custo vira
/// abatimento (sem dinheiro trocar de mãos) — o botão "Abater" saiu da tela.
/// </summary>
public static class CostBeneficiary
{
    /// <summary>Chave de comparação: sem acento, maiúsculas e espaços repetidos ("  José   da Silva" = "jose da silva").</summary>
    public static string Key(string? value)
    {
        var spaced = PlayerNames.NormalizeSpaces(value);
        if (spaced.Length == 0)
            return string.Empty;
        var decomposed = spaced.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    /// <summary>O "Para quem" do custo é o próprio jogador (nome completo ou apelido)?</summary>
    public static bool IsPlayer(string? beneficiary, Person? person)
    {
        var key = Key(beneficiary);
        if (key.Length == 0 || person is null)
            return false;
        return key == Key(person.FullName) || (!string.IsNullOrWhiteSpace(person.Nickname) && key == Key(person.Nickname));
    }

    /// <summary>Custo de verdade (não o caixa) cujo favorecido é o jogador</summary>
    public static bool IsPlayersOwnCost(CostExtra? cost, Person? person) =>
        cost is { IsCashBox: false } && IsPlayer(cost.Beneficiary, person);
}
