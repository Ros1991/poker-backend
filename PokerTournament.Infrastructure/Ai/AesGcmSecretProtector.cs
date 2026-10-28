using System.Security.Cryptography;
using System.Text;
using PokerTournament.Application.Interfaces;

namespace PokerTournament.Infrastructure.Ai;

/// <summary>
/// Cifra a chave da API da Anthropic com AES-256-GCM (SPEC-20261027-1312). Chave mestra: variável <c>AI_SECRETS_KEY</c>
/// (base64 de 32 bytes; em Development o valor só de desenvolvimento do appsettings.Development.json). Formato guardado:
/// base64( versão 1 ‖ nonce 12 ‖ texto cifrado ‖ tag 16 ), com dado associado fixo (amarra o texto cifrado a este uso).
/// Sem chave mestra válida: <see cref="IsConfigured"/> = false, cifrar lança e decifrar devolve null.
/// </summary>
public sealed class AesGcmSecretProtector : IAiSecretProtector
{
    public const string EnvironmentVariable = "AI_SECRETS_KEY";
    public const string ConfigurationKey = "Ai:SecretsKey";

    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("poker-da-galera:ai-settings:anthropic-api-key:v1");

    private readonly byte[]? _key;

    public AesGcmSecretProtector(string? masterKeyBase64)
    {
        _key = Parse(masterKeyBase64);
    }

    /// <summary>A chave mestra veio mas não é base64 de 32 bytes (para o log de subida).</summary>
    public static bool IsInvalid(string? masterKeyBase64) =>
        !string.IsNullOrWhiteSpace(masterKeyBase64) && Parse(masterKeyBase64) is null;

    public bool IsConfigured => _key is not null;

    public string Protect(string plaintext)
    {
        if (_key is null)
            throw new InvalidOperationException("Chave mestra AI_SECRETS_KEY ausente ou inválida.");
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[1 + NonceSize + plain.Length + TagSize];
        output[0] = Version;
        var nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(1 + NonceSize, plain.Length), output.AsSpan(1 + NonceSize + plain.Length, TagSize), AssociatedData);
        CryptographicOperations.ZeroMemory(plain);
        return Convert.ToBase64String(output);
    }

    public string? Unprotect(string ciphertext)
    {
        if (_key is null || string.IsNullOrEmpty(ciphertext))
            return null;
        try
        {
            var data = Convert.FromBase64String(ciphertext);
            if (data.Length < 1 + NonceSize + TagSize || data[0] != Version)
                return null;
            var cipherLength = data.Length - 1 - NonceSize - TagSize;
            var plain = new byte[cipherLength];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(data.AsSpan(1, NonceSize), data.AsSpan(1 + NonceSize, cipherLength), data.AsSpan(1 + NonceSize + cipherLength, TagSize), plain, AssociatedData);
            var text = Encoding.UTF8.GetString(plain);
            CryptographicOperations.ZeroMemory(plain);
            return text;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            // Chave mestra trocada ou texto adulterado: a tela pede para cadastrar a chave de novo
            return null;
        }
    }

    private static byte[]? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            var bytes = Convert.FromBase64String(value.Trim());
            return bytes.Length == 32 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
