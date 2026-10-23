namespace PokerTournament.Application.Common;

/// <summary>
/// Endereço público do frontend (variável PUBLIC_APP_URL), usado para montar links
/// que saem do sistema (e-mail de redefinição de senha, convites etc.).
/// </summary>
public class PublicAppOptions
{
    public const string DefaultUrl = "http://localhost:5173";

    /// <summary>Sem barra no fim, ex.: https://pokerdagalera.com.br</summary>
    public string Url { get; set; } = DefaultUrl;

    public string BuildUrl(string path) =>
        Url.TrimEnd('/') + "/" + path.TrimStart('/');
}

/// <summary>Configuração do envio de e-mail (seção "Email" do appsettings + RESEND_APITOKEN).</summary>
public class EmailOptions
{
    public const string DefaultFrom = "Poker da Galera <nao-responda@pokerdagalera.com.br>";

    public string? ResendApiToken { get; set; }
    public string From { get; set; } = DefaultFrom;
    /// <summary>Em Development, força o Resend mesmo assim (teste de envio real).</summary>
    public bool ForceResend { get; set; }
}
