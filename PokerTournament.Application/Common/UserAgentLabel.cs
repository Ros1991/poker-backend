namespace PokerTournament.Application.Common;

/// <summary>
/// Nome amigável do aparelho a partir do User-Agent gravado na sessão (auth_sessions.user_agent, até 300
/// caracteres) — "Chrome no Windows", "Safari no iPhone" — para a lista "Aparelhos conectados" do perfil
/// (SPEC-20261023-1211). Heurística simples, sem pacote: só serve para a pessoa reconhecer os próprios aparelhos.
/// </summary>
public static class UserAgentLabel
{
    public sealed record Parsed(string Browser, string Platform, string DeviceType, string Label);

    public const string UnknownBrowser = "Navegador";
    public const string UnknownPlatform = "aparelho desconhecido";

    public static Parsed Parse(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        var browser = BrowserOf(ua);
        var (platform, device) = PlatformOf(ua);
        var label = platform == UnknownPlatform
            ? (browser == UnknownBrowser ? "Aparelho desconhecido" : browser)
            : $"{browser} no {platform}";
        return new Parsed(browser, platform, device, label);
    }

    private static bool Has(string ua, string token) => ua.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string BrowserOf(string ua)
    {
        if (ua.Length == 0) return UnknownBrowser;
        // A ordem importa: Edge, Opera e Samsung também dizem "Chrome"; o Chrome também diz "Safari"
        if (Has(ua, "Edg/") || Has(ua, "EdgA/") || Has(ua, "EdgiOS/")) return "Edge";
        if (Has(ua, "OPR/") || Has(ua, "Opera")) return "Opera";
        if (Has(ua, "SamsungBrowser/")) return "Samsung Internet";
        if (Has(ua, "Firefox/") || Has(ua, "FxiOS/")) return "Firefox";
        if (Has(ua, "CriOS/") || Has(ua, "Chrome/")) return "Chrome";
        if (Has(ua, "Safari/") && Has(ua, "Version/")) return "Safari";
        if (Has(ua, "WhatsApp")) return "WhatsApp";
        if (Has(ua, "Instagram")) return "Instagram";
        return UnknownBrowser;
    }

    private static (string Platform, string DeviceType) PlatformOf(string ua)
    {
        if (ua.Length == 0) return (UnknownPlatform, "unknown");
        // "no iPhone", "no Android"… (a frase usa "no" para todos)
        if (Has(ua, "iPhone") || Has(ua, "iPod")) return ("iPhone", "mobile");
        if (Has(ua, "iPad")) return ("iPad", "tablet");
        if (Has(ua, "Android")) return ("Android", Has(ua, "Mobile") ? "mobile" : "tablet");
        if (Has(ua, "CrOS")) return ("Chromebook", "desktop");
        if (Has(ua, "Windows")) return ("Windows", "desktop");
        if (Has(ua, "Macintosh") || Has(ua, "Mac OS X")) return ("Mac", "desktop");
        if (Has(ua, "Linux")) return ("Linux", "desktop");
        return (UnknownPlatform, "unknown");
    }
}
