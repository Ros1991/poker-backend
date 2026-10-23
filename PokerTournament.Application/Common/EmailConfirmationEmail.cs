using System.Net;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Application.Common;

/// <summary>
/// Conteúdo do e-mail "Confirme seu e-mail" do cadastro (HTML simples com a logo no topo + versão
/// texto), no mesmo molde do <see cref="PasswordResetEmail"/>.
/// </summary>
public static class EmailConfirmationEmail
{
    public const string Subject = "Confirme seu e-mail — Poker da Galera";
    public const string TemplateKey = "email_confirmation";

    /// <param name="logoUrl">URL pública da logo completa (PUBLIC_APP_URL/brand/logo-completa.png); null = sem logo.</param>
    public static EmailMessage Build(string to, string fullName, string link, string? logoUrl = null)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? "jogador" : fullName.Trim();
        var safeName = WebUtility.HtmlEncode(name);
        var safeLink = WebUtility.HtmlEncode(link);
        // A logo vem do site (não anexada): o Gmail e a maioria dos clientes mostram imagens externas do remetente.
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
                  <h1 style="font-size:20px;margin:0 0 16px;color:#1B2E4B;">Confirme seu e-mail</h1>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 16px;">Olá, {safeName}.</p>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 24px;">Sua conta no Poker da Galera foi criada. Para liberar o acesso, confirme que este e-mail é seu clicando no botão abaixo:</p>
                  <p style="margin:0 0 24px;">
                    <a href="{safeLink}" style="display:inline-block;background:#1E5AA8;color:#ffffff;text-decoration:none;font-weight:bold;padding:12px 20px;border-radius:8px;">Confirmar e-mail</a>
                  </p>
                  <p style="font-size:13px;line-height:1.5;color:#5A6B82;margin:0 0 8px;">Se o botão não abrir, copie e cole este endereço no navegador:<br><a href="{safeLink}" style="color:#1E5AA8;word-break:break-all;">{safeLink}</a></p>
                  <p style="font-size:13px;line-height:1.5;color:#5A6B82;margin:16px 0 8px;">O link vale por 48 horas e só pode ser usado uma vez. Depois de confirmar, é só entrar com o seu e-mail e a senha que você escolheu.</p>
                  <p style="font-size:13px;line-height:1.5;color:#5A6B82;margin:0;">Se não foi você que criou a conta, ignore este e-mail; sem a confirmação ninguém consegue entrar com ele.</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var text = $"""
            Confirme seu e-mail — Poker da Galera

            Olá, {name}.

            Sua conta no Poker da Galera foi criada. Para liberar o acesso, confirme que este e-mail é seu
            abrindo este endereço:

            {link}

            O link vale por 48 horas e só pode ser usado uma vez.
            Se não foi você que criou a conta, ignore este e-mail; sem a confirmação ninguém consegue entrar com ele.
            """;

        return new EmailMessage(to, Subject, html, text);
    }
}
