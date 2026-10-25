using System.Net;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Application.Common;

/// <summary>
/// E-mail "Definir sua senha" (SPEC-20261023-1211): conta que só entra pelo Google pede, em "Meu perfil", um link
/// para criar a senha. O link é o MESMO da redefinição (SPEC-20261023-1203: <c>/redefinir-senha/{token}</c>, 1 hora,
/// uso único) — só o texto muda, porque a conta ainda não tem senha. Mesmo molde e saudação fixa do
/// <see cref="PasswordResetEmail"/>.
/// </summary>
public static class PasswordSetupEmail
{
    public const string Subject = "Definir sua senha — Poker da Galera";
    public const string TemplateKey = "password_setup";
    public const string Greeting = "Olá!";

    /// <param name="logoUrl">URL pública da logo completa (PUBLIC_APP_URL/brand/logo-completa.png); null = sem logo.</param>
    public static EmailMessage Build(string to, string link, string? logoUrl = null)
    {
        var safeLink = WebUtility.HtmlEncode(link);
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
                  <h1 style="font-size:20px;margin:0 0 16px;color:#1B2E4B;">Definir sua senha</h1>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 16px;">{Greeting}</p>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 24px;">Você pediu, em "Meu perfil", uma senha para entrar no Poker da Galera também com e-mail e senha. Clique no botão abaixo para criar a sua:</p>
                  <p style="margin:0 0 24px;">
                    <a href="{safeLink}" style="display:inline-block;background:#1E5AA8;color:#ffffff;text-decoration:none;font-weight:bold;padding:12px 20px;border-radius:8px;">Criar minha senha</a>
                  </p>
                  <p style="font-size:13px;line-height:1.5;color:#5A6B82;margin:0 0 8px;">Se o botão não abrir, copie e cole este endereço no navegador:<br><a href="{safeLink}" style="color:#1E5AA8;word-break:break-all;">{safeLink}</a></p>
                  <p style="font-size:13px;line-height:1.5;color:#5A6B82;margin:16px 0 8px;">O link vale por 1 hora e só pode ser usado uma vez. Depois de criar a senha, entre de novo nos seus aparelhos.</p>
                  <p style="font-size:13px;line-height:1.5;color:#5A6B82;margin:0;">Se não foi você, ignore este e-mail: nada muda na sua conta.</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var text = $"""
            Definir sua senha — Poker da Galera

            {Greeting}

            Você pediu, em "Meu perfil", uma senha para entrar no Poker da Galera também com e-mail e senha.
            Abra este endereço para criar a sua:

            {link}

            O link vale por 1 hora e só pode ser usado uma vez. Depois de criar a senha, entre de novo nos seus aparelhos.
            Se não foi você, ignore este e-mail: nada muda na sua conta.
            """;

        return new EmailMessage(to, Subject, html, text);
    }
}
