using System.Security.Cryptography;
using Application.Abstractions.Services;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

internal sealed class FileEncryptionService(
    IOptions<EncryptionSettings> encryptionSettings,
    IOptions<FileStorageSettings> storageSettings) : IFileEncryptionService
{
    public async Task<EncryptedFileResult> EncryptAndSaveAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        byte[] key = DeriveKey(encryptionSettings.Value.Key);
        byte[] iv = RandomNumberGenerator.GetBytes(12); // 96-bit nonce for GCM

        string encryptedDir = Path.Combine(storageSettings.Value.BasePath, "encrypted");
        Directory.CreateDirectory(encryptedDir);

        string encryptedPath = Path.Combine(encryptedDir, UniqueStoredName(fileName));

        using MemoryStream plaintextStream = new();
        await fileStream.CopyToAsync(plaintextStream, cancellationToken);
        byte[] plaintext = plaintextStream.ToArray();

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16]; // 128-bit auth tag

        using var aesGcm = new AesGcm(key, 16);
        aesGcm.Encrypt(iv, plaintext, ciphertext, tag);

        // Store format: [tag (16 bytes)][ciphertext]
        // `CreateNew`: două fișiere nu pot ajunge niciodată la aceeași cale. Suprascrierea ar
        // însemna un fișier al cărui conținut nu se mai potrivește cu IV-ul salvat în baza de date,
        // adică un document care nu se mai poate decripta deloc.
        await using FileStream outputStream = new(encryptedPath, FileMode.CreateNew, FileAccess.Write);
        await outputStream.WriteAsync(tag, cancellationToken);
        await outputStream.WriteAsync(ciphertext, cancellationToken);

        string ivBase64 = Convert.ToBase64String(iv);

        return new EncryptedFileResult(encryptedPath, ivBase64);
    }

    public async Task<Stream> DecryptAndReadAsync(
        string encryptedFilePath,
        string iv,
        CancellationToken cancellationToken)
    {
        byte[] key = DeriveKey(encryptionSettings.Value.Key);
        byte[] ivBytes = Convert.FromBase64String(iv);

        byte[] encryptedData = await File.ReadAllBytesAsync(encryptedFilePath, cancellationToken);

        // Extract tag and ciphertext
        byte[] tag = encryptedData[..16];
        byte[] ciphertext = encryptedData[16..];
        byte[] plaintext = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(key, 16);
        aesGcm.Decrypt(ivBytes, ciphertext, tag, plaintext);

        return new MemoryStream(plaintext);
    }

    /// <summary>
    /// Numele fișierului criptat de pe disc: cel dat de apelant, plus un id unic.
    /// </summary>
    /// <remarks>
    /// Apelanții trimit nume citibile de om („specimen-semnatura.png”, numele fișierului încărcat
    /// de utilizator), care se repetă între conturi. Fără id, al doilea fișier îl suprascria pe
    /// primul, iar primul document rămânea cu un IV care nu se mai potrivește cu conținutul —
    /// exact eroarea „The computed authentication tag did not match”. Numele original rămâne în
    /// baza de date, pe document; aici contează doar unicitatea.
    /// </remarks>
    internal static string UniqueStoredName(string fileName)
    {
        // Curățarea nu se bazează pe `Path.GetInvalidFileNameChars`: pe Linux lista are doar „/”,
        // așa că un nume venit de pe Windows ar putea ajunge pe disc cu „:” sau „*” în el.
        string name = fileName ?? string.Empty;
        int separator = name.LastIndexOfAny(['/', '\\']);
        name = separator >= 0 ? name[(separator + 1)..] : name;

        int dot = name.LastIndexOf('.');
        string extension = dot > 0 ? Keep(name[(dot + 1)..]) : string.Empty;
        string stem = Keep(dot > 0 ? name[..dot] : name);

        if (stem.Length > 60)
        {
            stem = stem[..60];
        }

        string prefix = stem.Length == 0 ? "fisier" : stem;
        string suffix = extension.Length == 0 ? string.Empty : $".{extension}";

        return $"{prefix}-{Guid.NewGuid():N}{suffix}.enc";
    }

    /// <summary>Litere, cifre, spațiu, punct, liniuță și underscore; restul devin „_”.</summary>
    private static string Keep(string value) => string.Concat(value.Select(c =>
        char.IsLetterOrDigit(c) || c is ' ' or '.' or '-' or '_' ? c : '_'));

    private static byte[] DeriveKey(string keyString)
    {
        // Use SHA-256 to derive a 256-bit key from the configured secret
        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(keyString));
    }
}
