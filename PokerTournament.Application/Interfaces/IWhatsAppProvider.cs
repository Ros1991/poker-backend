namespace PokerTournament.Application.Interfaces;

public interface IWhatsAppProvider
{
    string GenerateWhatsAppLink(string phoneNumber, string message);
    string BuildMessage(string templateKey, Dictionary<string, string> data);
}
