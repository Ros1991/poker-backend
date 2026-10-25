using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Common;

/// <summary>
/// Conferência das imagens enviadas (foto de pessoa e logo do clube): só JPEG, PNG ou WebP, reconhecidos pela
/// ASSINATURA do arquivo (não pela extensão nem pelo Content-Type que o cliente diz), até 2 MB. O servidor não
/// reprocessa a imagem: o navegador já manda a foto recortada e comprimida (SPEC-20261023-1213, F2).
/// </summary>
public static class ImageFiles
{
    /// <summary>Limite de tamanho aceito (2 MB).</summary>
    public const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Teto do corpo da requisição de upload ([RequestSizeLimit]): bem acima dos 2 MB para que uma foto grande de
    /// celular (3–8 MB, quando a compressão do navegador não rodou) receba o 400 com a mensagem acima, e não um erro
    /// de leitura do formulário. Acima disso o servidor corta a conexão.
    /// </summary>
    public const long MaxRequestBytes = 10 * 1024 * 1024;

    public const string InvalidPhotoMessage = "A foto precisa ser JPG, PNG ou WebP de até 2 MB.";
    public const string InvalidLogoMessage = "A logo precisa ser JPG, PNG ou WebP de até 2 MB.";

    /// <summary>Tipo reconhecido: extensão com que o arquivo é gravado e o Content-Type servido.</summary>
    public sealed record ImageKind(string Extension, string ContentType);

    public static readonly ImageKind Jpeg = new(".jpg", "image/jpeg");
    public static readonly ImageKind Png = new(".png", "image/png");
    public static readonly ImageKind Webp = new(".webp", "image/webp");

    /// <summary>Tipo pela assinatura (primeiros bytes); null quando não é JPEG, PNG nem WebP.</summary>
    public static ImageKind? Detect(ReadOnlySpan<byte> data)
    {
        // JPEG: FF D8 FF
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return Jpeg;
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            return Png;
        // WebP: "RIFF" .... "WEBP"
        if (data.Length >= 12
            && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            return Webp;
        return null;
    }

    /// <summary>
    /// Lê o envio inteiro (no máximo <see cref="MaxBytes"/>) e confere a assinatura. Vazio, maior que 2 MB ou fora de
    /// JPEG/PNG/WebP → <see cref="DomainException"/> (400) com <paramref name="invalidMessage"/>.
    /// </summary>
    public static async Task<(byte[] Data, ImageKind Kind)> ReadValidatedAsync(
        Stream stream, long declaredLength, string invalidMessage, CancellationToken ct = default)
    {
        if (declaredLength <= 0 || declaredLength > MaxBytes)
            throw new DomainException(invalidMessage);

        using var buffer = new MemoryStream((int)Math.Min(declaredLength, MaxBytes));
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                throw new DomainException(invalidMessage);
            buffer.Write(chunk, 0, read);
        }

        var data = buffer.ToArray();
        var kind = data.Length == 0 ? null : Detect(data);
        if (kind is null)
            throw new DomainException(invalidMessage);
        return (data, kind);
    }
}
