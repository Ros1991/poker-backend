namespace PokerTournament.Application.Common;

/// <summary>
/// Pasta ÚNICA dos arquivos enviados (fotos de pessoa e logo do clube), usada pelo <c>Program.cs</c> (que serve
/// <see cref="RequestPath"/>) e por todo upload — antes o upload gravava em <c>Directory.GetCurrentDirectory()</c>
/// e o <c>UseStaticFiles</c> servia de <c>ContentRootPath</c>, que divergem quando o processo não roda da pasta
/// do projeto. Em produção (Railway) é <c>/app/uploads</c>, um volume persistente: as fotos sobrevivem aos deploys
/// (SPEC-20261023-1213, ajuste do orquestrador à F1 — nada de fotos no Postgres).
/// </summary>
public sealed class UploadsOptions
{
    /// <summary>Variável de ambiente que troca a pasta (o executor de testes aponta cada rodada para a sua).</summary>
    public const string EnvVar = "UPLOADS_PATH";

    /// <summary>Caminho público em que a pasta é servida (<c>/uploads/photos/...</c>, <c>/uploads/logos/...</c>).</summary>
    public const string RequestPath = "/uploads";

    public const string PhotosFolder = "photos";
    public const string LogosFolder = "logos";

    public UploadsOptions(string rootPath)
    {
        RootPath = rootPath;
    }

    /// <summary>Caminho absoluto da pasta de uploads no disco.</summary>
    public string RootPath { get; }

    /// <summary><c>UPLOADS_PATH</c> quando definido; senão <c>{ContentRootPath}/uploads</c>.</summary>
    public static string ResolveRoot(string contentRootPath)
    {
        var custom = Environment.GetEnvironmentVariable(EnvVar);
        return string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(contentRootPath, "uploads")
            : custom.Trim();
    }

    /// <summary>Pasta de uma categoria (photos/logos), criada se não existir.</summary>
    public string EnsureFolder(string folder)
    {
        var path = Path.Combine(RootPath, folder);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Arquivo no disco de uma URL nossa (<c>/uploads/{pasta}/{arquivo}</c>), ou null para qualquer outra coisa
    /// (URL externa, caminho com <c>..</c>, outra pasta). Usado para apagar o arquivo anterior ao trocar.
    /// </summary>
    public string? LocalFileFor(string? url, string folder)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var prefix = $"{RequestPath}/{folder}/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var name = url[prefix.Length..];
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Contains("..", StringComparison.Ordinal))
            return null;
        return Path.Combine(RootPath, folder, name);
    }
}
