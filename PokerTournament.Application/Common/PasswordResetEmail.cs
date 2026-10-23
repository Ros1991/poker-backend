using System.Net;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Application.Common;

/// <summary>Conteúdo do e-mail "Redefinir sua senha" (HTML simples + versão texto, sem logo).</summary>
public static class PasswordResetEmail
{
    public const string Subject = "Redefinir sua senha — Poker da Galera";
    public const string TemplateKey = "password_reset";

    public static EmailMessage Build(string to, string fullName, string link)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? "jogador" : fullName.Trim();
        var safeName = WebUtility.HtmlEncode(name);
        var safeLink = WebUtility.HtmlEncode(link);

        var html = $"""
            <!doctype html>
            <html lang="pt-BR">
            <body style="margin:0;padding:24px;background:#f4f4f5;font-family:Arial,Helvetica,sans-serif;color:#18181b;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:520px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px;">
                <tr><td>
                  <h1 style="font-size:20px;margin:0 0 16px;">Redefinir sua senha</h1>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 16px;">Olá, {safeName}.</p>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 24px;">Recebemos um pedido para redefinir a senha da sua conta no Poker da Galera. Clique no botão abaixo para criar uma senha nova:</p>
                  <p style="margin:0 0 24px;">
                    <a href="{safeLink}" style="display:inline-block;background:#2563eb;color:#ffffff;text-decoration:none;font-weight:bold;padding:12px 20px;border-radius:8px;">Criar senha nova</a>
                  </p>
                  <p style="font-size:13px;line-height:1.5;color:#52525b;margin:0 0 8px;">Se o botão não abrir, copie e cole este endereço no navegador:<br><a href="{safeLink}" style="color:#2563eb;word-break:break-all;">{safeLink}</a></p>
                  <p style="font-size:13px;line-height:1.5;color:#52525b;margin:16px 0 8px;">O link vale por 1 hora e só pode ser usado uma vez.</p>
                  <p style="font-size:13px;line-height:1.5;color:#52525b;margin:0;">Se não foi você, ignore este e-mail; sua senha continua a mesma.</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var text = $"""
            Redefinir sua senha — Poker da Galera

            Olá, {name}.

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
