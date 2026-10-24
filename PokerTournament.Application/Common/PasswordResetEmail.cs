using System.Net;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Application.Common;

/// <summary>
/// Conteúdo do e-mail "Redefinir sua senha" (HTML simples com a logo no topo + versão texto). A saudação é fixa
/// ("Olá!"), como no <see cref="EmailConfirmationEmail"/>: o pedido é anônimo, sai também para conta ainda não
/// confirmada (D16.5) e o nome da conta pode ter sido digitado por qualquer um no cadastro — nome no corpo faria do
/// "Esqueci minha senha" um jeito de mandar texto livre (phishing) com o remetente e a logo oficiais.
/// </summary>
public static class PasswordResetEmail
{
    public const string Subject = "Redefinir sua senha — Poker da Galera";
    public const string TemplateKey = "password_reset";
    public const string Greeting = "Olá!";

    /// <param name="logoUrl">URL pública da logo completa (PUBLIC_APP_URL/brand/logo-completa.png); null = sem logo.</param>
    public static EmailMessage Build(string to, string link, string? logoUrl = null)
    {
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
                  <h1 style="font-size:20px;margin:0 0 16px;color:#1B2E4B;">Redefinir sua senha</h1>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 16px;">{Greeting}</p>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 24px;">Recebemos um pedido para redefinir a senha da sua conta no Poker da Galera. Clique no botão abaixo para criar uma senha nova:</p>
                  <p style="margin:0 0 24px;">
                    <a href="{safeLink}" style="display:inline-block;background:#1E5AA8;color:#ffffff;text-decoration:none;font-weight:bold;padding:12px 20px;border-radius:8px;">Criar senha nova</a>
                  </p>
                  <p style="font-size:13px;line-height:1.5;color:#5A6B82;margin:0 0 8px;">Se o botão não abrir, copie e cole este endereço no navegador:<br><a href="{safeLink}" style="color:#1E5AA8;word-break:break-all;">{safeLink}</a></p>
                  <p style="font-size:13px;line-height:1.5;color:#5A6B82;margin:16px 0 8px;">O link vale por 1 hora e só pode ser usado uma vez.</p>
                  <p style="font-size:13px;line-height:1.5;color:#5A6B82;margin:0;">Se não foi você, ignore este e-mail; sua senha continua a mesma.</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var text = $"""
            Redefinir sua senha — Poker da Galera

            {Greeting}

            Recebemos um pedido para redefinir a senha da sua conta no Poker da Galera.
            Abra este endereço para criar uma senha nova:

            {link}

            O link vale por 1 hora e só pode ser usado uma vez.
            Se não foi você, ignore este e-mail; sua senha continua a mesma.
            """;

        return new EmailMessage(to, Subject, html, text);
    }

    /// <summary>ro***@gmail.com — para registrar o destinatário sem expor o e-mail inteiro.</summary>
    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
            return "***";
        var local = email[..at];
        var visible = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visible}***{email[at..]}";
    }
}
