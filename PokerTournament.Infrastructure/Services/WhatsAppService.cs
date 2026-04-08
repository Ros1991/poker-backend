using System.Text.RegularExpressions;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Infrastructure.Services;

public partial class WhatsAppService : IWhatsAppProvider
{
    private static readonly Dictionary<string, string> Templates = new()
    {
        ["buyin"] =
            "Torneio: {tournament_name}\n" +
            "Jogador: {player_name}\n" +
            "Buy-in: R$ {amount}\n" +
            "Chave Pix: {pix_key}\n" +
            "Beneficiario: {pix_beneficiary}\n\n" +
            "Por favor, realize o pagamento e envie o comprovante.",

        ["rebuy"] =
            "Torneio: {tournament_name}\n" +
            "Jogador: {player_name}\n" +
            "Rebuy #{rebuy_number}: R$ {amount}\n" +
            "Chave Pix: {pix_key}\n" +
            "Beneficiario: {pix_beneficiary}\n\n" +
            "Por favor, realize o pagamento e envie o comprovante.",

        ["addon"] =
            "Torneio: {tournament_name}\n" +
            "Jogador: {player_name}\n" +
            "Add-on: R$ {amount}\n" +
            "Chave Pix: {pix_key}\n" +
            "Beneficiario: {pix_beneficiary}\n\n" +
            "Por favor, realize o pagamento e envie o comprovante.",

        ["settlement_close"] =
            "Torneio: {tournament_name}\n\n" +
            "O acerto financeiro foi encerrado.\n" +
            "Total arrecadado: R$ {total_collected}\n" +
            "Premio liquido: R$ {net_prize}\n\n" +
            "Obrigado pela participacao!",

        ["payment_confirmed"] =
            "Torneio: {tournament_name}\n" +
            "Jogador: {player_name}\n\n" +
            "Pagamento de R$ {amount} confirmado.\n" +
            "Metodo: {payment_method}\n\n" +
            "Obrigado!",

        ["elimination"] =
            "Torneio: {tournament_name}\n" +
            "Jogador: {player_name}\n" +
            "Posicao final: {position}\n\n" +
            "{prize_info}"
    };

    public string GenerateWhatsAppLink(string phoneNumber, string message)
    {
        var cleaned = CleanPhoneNumber(phoneNumber);

        // Add Brazil country code if not present
        if (!cleaned.StartsWith("55"))
            cleaned = "55" + cleaned;

        var encodedMessage = Uri.EscapeDataString(message);
        return $"https://wa.me/{cleaned}?text={encodedMessage}";
    }

    public string BuildMessage(string templateKey, Dictionary<string, string> data)
    {
        if (!Templates.TryGetValue(templateKey, out var template))
            throw new ArgumentException($"Template '{templateKey}' not found.", nameof(templateKey));

        var message = template;
        foreach (var (key, value) in data)
        {
            message = message.Replace($"{{{key}}}", value);
        }

        return message;
    }

    private static string CleanPhoneNumber(string phoneNumber)
    {
        return NonDigitRegex().Replace(phoneNumber, "");
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitRegex();
}
