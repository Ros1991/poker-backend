using Microsoft.Extensions.Logging;
using PokerTournament.Application.Common;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Services;

/// <summary>
/// Foto de pessoa e logo do clube no disco (SPEC-20261023-1213; F1 do main.md substituída pelo ajuste do
/// orquestrador: a pasta de uploads é o volume persistente <c>/app/uploads</c> do Railway, nada de fotos no Postgres).
/// <list type="bullet">
/// <item>Confere tipo pela assinatura (JPEG/PNG/WebP) e tamanho (2 MB) — <see cref="ImageFiles"/>.</item>
/// <item>Cada envio grava um arquivo com nome NOVO (<c>{dono}-{aleatório}.ext</c>): a URL muda a cada troca, então o
/// cache longo de <c>/uploads/photos</c> nunca mostra foto velha; o arquivo anterior é apagado depois da gravação.</item>
/// <item>Genérico pelo dono (pessoa hoje; a conta do usuário na SPEC-20261023-1211 reaproveita <see cref="SaveAsync"/>).</item>
/// </list>
/// Nenhuma checagem de plano (F6): foto e logo valem em todos os planos.
/// </summary>
public class PhotoService
{
    private readonly IAppDbContext _db;
    private readonly UploadsOptions _uploads;
    private readonly ILogger<PhotoService> _logger;

    public PhotoService(IAppDbContext db, UploadsOptions uploads, ILogger<PhotoService> logger)
    {
        _db = db;
        _uploads = uploads;
        _logger = logger;
    }

    /// <summary>
    /// Troca a foto da pessoa: valida, grava o arquivo novo, põe <c>PhotoUrl</c> e salva (junto com o que
    /// <paramref name="alsoSave"/> acrescentar ao contexto, ex.: a atividade do histórico). Apaga a foto anterior.
    /// Devolve a URL nova (<c>/uploads/photos/...</c>).
    /// </summary>
    public async Task<string> SetPersonPhotoAsync(
        Person person, Stream data, long length, Action? alsoSave = null, CancellationToken ct = default)
    {
        var previous = person.PhotoUrl;
        var url = await SaveAsync(UploadsOptions.PhotosFolder, person.Id, data, length, ImageFiles.InvalidPhotoMessage,
            u => person.PhotoUrl = u, alsoSave, ct);
        DeleteQuietly(previous, UploadsOptions.PhotosFolder);
        return url;
    }

    /// <summary>Tira a foto da pessoa (PhotoUrl = null) e apaga o arquivo.</summary>
    public async Task RemovePersonPhotoAsync(Person person, CancellationToken ct = default)
    {
        var previous = person.PhotoUrl;
        person.PhotoUrl = null;
        await _db.SaveChangesAsync(ct);
        DeleteQuietly(previous, UploadsOptions.PhotosFolder);
    }

    /// <summary>
    /// Troca a logo do clube (<c>HomeGame.LogoUrl</c>). A logo vem do navegador na proporção original (sem recorte
    /// redondo), lado maior até 512 px, transparência preservada em PNG/WebP.
    /// </summary>
    public async Task<string> SetHomeGameLogoAsync(
        HomeGame homeGame, Stream data, long length, CancellationToken ct = default)
    {
        var previous = homeGame.LogoUrl;
        var url = await SaveAsync(UploadsOptions.LogosFolder, homeGame.Id, data, length, ImageFiles.InvalidLogoMessage,
            u => homeGame.LogoUrl = u, null, ct);
        DeleteQuietly(previous, UploadsOptions.LogosFolder);
        return url;
    }

    /// <summary>Tira a logo do clube e apaga o arquivo.</summary>
    public async Task RemoveHomeGameLogoAsync(HomeGame homeGame, CancellationToken ct = default)
    {
        var previous = homeGame.LogoUrl;
        homeGame.LogoUrl = null;
        await _db.SaveChangesAsync(ct);
        DeleteQuietly(previous, UploadsOptions.LogosFolder);
    }

    /// <summary>
    /// Valida e grava o arquivo em <c>{uploads}/{folder}/{ownerId}-{aleatório}{ext}</c>, aplica a URL com
    /// <paramref name="applyUrl"/> e salva. Se a gravação no banco falhar, o arquivo novo é apagado.
    /// </summary>
    public async Task<string> SaveAsync(
        string folder, Guid ownerId, Stream data, long length, string invalidMessage,
        Action<string> applyUrl, Action? alsoSave = null, CancellationToken ct = default)
    {
        var (bytes, kind) = await ImageFiles.ReadValidatedAsync(data, length, invalidMessage, ct);

        var directory = _uploads.EnsureFolder(folder);
        var fileName = $"{ownerId:N}-{Guid.NewGuid():N}{kind.Extension}";
        var filePath = Path.Combine(directory, fileName);
        await using (var file = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await file.WriteAsync(bytes, ct);

        var url = $"{UploadsOptions.RequestPath}/{folder}/{fileName}";
        try
        {
            applyUrl(url);
            alsoSave?.Invoke();
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            TryDelete(filePath);
            throw;
        }
        return url;
    }

    private void DeleteQuietly(string? url, string folder)
    {
        var path = _uploads.LocalFileFor(url, folder);
        if (path is not null)
            TryDelete(path);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            // Arquivo órfão não quebra a troca de foto: só fica registrado
            _logger.LogWarning(ex, "Não consegui apagar o arquivo antigo {Path}", path);
        }
    }
}
