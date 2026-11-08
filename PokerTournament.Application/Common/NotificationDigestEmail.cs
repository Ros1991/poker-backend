using System.Globalization;
using System.Net;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Application.Common;

/// <summary>Um item do e-mail resumo (SPEC-20261108-1635): já com o link absoluto do site.</summary>
public record DigestItem(string Title, string Body, string? Url, DateTimeOffset CreatedAt);

/// <summary>
/// Conteúdo do e-mail resumo de notificações (HTML com a logo no topo + versão texto), no mesmo molde do
/// <see cref="EmailConfirmationEmail"/>. Saudação fixa "Olá!" e todo texto das notificações passa por HtmlEncode: título e
/// corpo carregam nomes de torneio e de jogador digitados por organizadores.
/// </summary>
public static class NotificationDigestEmail
{
    public const string TemplateKey = "notification_digest";
    public const string Greeting = "Olá!";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Subject(int count) =>
        count == 1 ? "Você tem 1 novidade — Poker da Galera" : $"Você tem {count} novidades — Poker da Galera";

    /// <param name="totalUnread">Quantas não lidas a conta tem (pode ser mais que os itens listados).</param>
    /// <param name="siteUrl">Endereço público do site (PUBLIC_APP_URL), sem barra no fim.</param>
    /// <param name="logoUrl">URL pública da logo completa; null = sem logo.</param>
    public static EmailMessage Build(string to, IReadOnlyList<DigestItem> items, int totalUnread, string siteUrl, string? logoUrl = null)
    {
        var site = siteUrl.TrimEnd('/');
        var safeSite = WebUtility.HtmlEncode(site);
        var preferencesUrl = site + "/meu-perfil#notificacoes";
        var safePreferences = WebUtility.HtmlEncode(preferencesUrl);
        var remaining = Math.Max(0, totalUnread - items.Count);
        var remainingLabel = remaining == 1 ? "não lida" : "não lidas";
        var logo = string.IsNullOrWhiteSpace(logoUrl)
            ? string.Empty
            : $"""<img src="{WebUtility.HtmlEncode(logoUrl)}" alt="Poker da Galera" width="180" style="display:block;width:180px;max-width:100%;height:auto;margin:0 auto 20px;border:0;">""";

        var rows = string.Join("\n", items.Select(item =>
        {
            var title = WebUtility.HtmlEncode(item.Title);
            var body = WebUtility.HtmlEncode(item.Body);
            var when = WebUtility.HtmlEncode(FormatWhen(item.CreatedAt));
            var heading = item.Url is null
                ? $"""<span style="font-weight:bold;color:#1B2E4B;">{title}</span>"""
                : $"""<a href="{WebUtility.HtmlEncode(item.Url)}" style="font-weight:bold;color:#1E5AA8;text-decoration:none;">{title}</a>""";
            return $"""
                      <tr><td style="padding:12px 0;border-top:1px solid #E1E6EE;">
                        <p style="font-size:15px;line-height:1.4;margin:0 0 4px;">{heading}</p>
                        <p style="font-size:14px;line-height:1.5;margin:0 0 4px;color:#1B2E4B;">{body}</p>
                        <p style="font-size:12px;line-height:1.4;margin:0;color:#5A6B82;">{when}</p>
                      </td></tr>
                """;
        }));

        var more = remaining > 0
            ? $"""<p style="font-size:14px;line-height:1.5;margin:16px 0 0;color:#5A6B82;">E mais {remaining} {remainingLabel} no site.</p>"""
            : string.Empty;

        var html = $"""
            <!doctype html>
            <html lang="pt-BR">
            <body style="margin:0;padding:24px;background:#F5F7FA;font-family:Arial,Helvetica,sans-serif;color:#1B2E4B;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:520px;margin:0 auto;background:#ffffff;border:1px solid #E1E6EE;border-radius:12px;padding:32px;">
                <tr><td>
                  {logo}
                  <h1 style="font-size:20px;margin:0 0 16px;color:#1B2E4B;">Suas novidades</h1>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 16px;">{Greeting}</p>
                  <p style="font-size:15px;line-height:1.5;margin:0 0 16px;">Enquanto você esteve fora, aconteceu isto no Poker da Galera:</p>
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="border-bottom:1px solid #E1E6EE;">
            {rows}
                  </table>
                  {more}
                  <p style="margin:24px 0;">
                    <a href="{safeSite}/inicio" style="display:inline-block;background:#1E5AA8;color:#ffffff;text-decoration:none;font-weight:bold;padding:12px 20px;border-radius:8px;">Abrir o site</a>
                  </p>
                  <p style="font-size:13px;line-height:1.5;color:#5A6B82;margin:0;">Você recebe este resumo no máximo uma vez por hora, só com o que ainda não viu. Para parar ou escolher o que receber, abra <a href="{safePreferences}" style="color:#1E5AA8;">Meu perfil → Notificações</a>.</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var textItems = string.Join("\n\n", items.Select(item =>
            $"- {item.Title}\n  {item.Body}\n  {FormatWhen(item.CreatedAt)}" + (item.Url is null ? string.Empty : $"\n  {item.Url}")));
        var textMore = remaining > 0 ? $"\n\nE mais {remaining} {remainingLabel} no site." : string.Empty;
        var text = $"""
            Suas novidades — Poker da Galera

            {Greeting}

            Enquanto você esteve fora, aconteceu isto no Poker da Galera:

            {textItems}{textMore}

            Abrir o site: {site}/inicio

            Você recebe este resumo no máximo uma vez por hora, só com o que ainda não viu.
            Para parar ou escolher o que receber, abra Meu perfil → Notificações: {preferencesUrl}
            """;

        return new EmailMessage(to, Subject(totalUnread), html, text);
    }

    /// <summary>Data e hora no horário de Brasília ("08/11/2026 às 17:30").</summary>
    public static string FormatWhen(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, BrazilTimeZone.Value);
        return local.ToString("dd/MM/yyyy 'às' HH:mm", PtBr);
    }

    private static readonly Lazy<TimeZoneInfo> BrazilTimeZone = new(() =>
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("America/Sao_Paulo", out var tz)) return tz;
        if (TimeZoneInfo.TryFindSystemTimeZoneById("E. South America Standard Time", out tz)) return tz;
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília", "Brasília");
    });
}
