using System.Net;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Application.Common;

/// <summary>
/// Aviso "O Google foi conectado à sua conta" (SPEC-20261023-1210, pesquisa §10): sai quando o login social
/// é vinculado SOZINHO a uma conta confirmada (<c>@gmail.com</c>). Mesmo molde do <see cref="PasswordResetEmail"/>;
/// o botão leva ao "Esqueci minha senha" (quem não reconhece o vínculo troca a senha por lá).
/// </summary>
public static class ExternalLoginLinkedEmail
{
    public const string TemplateKey = "external_login_linked";

    public static string SubjectFor(string connectedTitle) => $"{connectedTitle} — Poker da Galera";

    /// <param name="connectedTitle">"O Google foi conectado à sua conta" (ExternalProviderDefinition.ConnectedTitle).</param>
    /// <param name="forgotPasswordLink">{PUBLIC_APP_URL}/esqueci-senha</param>
    /// <param name="logoUrl">URL pública da logo completa; null = sem logo.</param>
    public static EmailMessage Build(
        string to, string fullName, string providerDisplayName, string connectedTitle,
        string forgotPasswordLink, bool hasPassword, string? logoUrl = null)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? "jogador" : fullName.Trim();
        var safeName = WebUtility.HtmlEncode(name);
        var safeLink = WebUtility.HtmlEncode(forgotPasswordLink);
        var safeTitle = WebUtility.HtmlEncode(connectedTitle);
        var safeProvider = WebUtility.HtmlEncode(providerDisplayName);
        // Conta sem senha (só-social) não tem "senha que continua valendo": o link vira "definir senha"
        var passwordNote = hasPassword ? " Sua senha continua valendo." : string.Empty;
        var action = hasPassword ? "Troque a senha agora" : "Defina uma senha agora";
        var button = hasPassword ? "Trocar a senha" : "Definir uma senha";
        var logo = string.IsNullOrWhiteSpace(logoUrl)
            ? string.Empty
            : $"""<img src="{WebUtility.HtmlEncode(logoUrl)}" alt="Poker da Galera" width="180" style="display:block;width:180px;max-width:100%;height:auto;margin:0 auto 20px;border:0;">""";

        var html = $"""
            <!doctype html>
            <html lang="pt-BR">
            <body style="margin:0;padding:24px;background:#F5F7FA;font-family:Arial,Helvetica,sans-serif;color:#1B2E4B;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:520px;margin:0 auto;background:#ffffff;border:1px solid #E1E6EE;border-radius:12px;padding:32px;">
                <tr><td>
                  {logo}
                  <h1 style="font-size:20px;margin:0 0 16px;color:#1B2E4B;">{safeTitle}</h1>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 16px;">Olá, {safeName}.</p>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 24px;">Sua conta no Poker da Galera agora também entra com o botão "Continuar com {safeProvider}".{passwordNote}</p>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 24px;"><strong>Não foi você?</strong> {action}:</p>
                  <p style="margin:0 0 24px;">
                    <a href="{safeLink}" style="display:inline-block;background:#1E5AA8;color:#ffffff;text-decoration:none;font-weight:bold;padding:12px 20px;border-radius:8px;">{button}</a>
                  </p>
                  <p style="font-size:13px;line-height:1.5;color:#5A6B82;margin:0;">Se o botão não abrir, copie e cole este endereço no navegador:<br><a href="{safeLink}" style="color:#1E5AA8;word-break:break-all;">{safeLink}</a></p>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var text = $"""
            {connectedTitle} — Poker da Galera

            Olá, {name}.

            Sua conta no Poker da Galera agora também entra com o botão "Continuar com {providerDisplayName}".{passwordNote}

            Não foi você? {action}:

            {forgotPasswordLink}
            """;

        return new EmailMessage(to, SubjectFor(connectedTitle), html, text);
    }
}
