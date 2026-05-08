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

        string encryptedPath = Path.Combine(encryptedDir, fileName + ".enc");

        using MemoryStream plaintextStream = new();
        await fileStream.CopyToAsync(plaintextStream, cancellationToken);
        byte[] plaintext = plaintextStream.ToArray();

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16]; // 128-bit auth tag

        using var aesGcm = new AesGcm(key, 16);
        aesGcm.Encrypt(iv, plaintext, ciphertext, tag);

        // Store format: [tag (16 bytes)][ciphertext]
        await using FileStream outputStream = new(encryptedPath, FileMode.Create, FileAccess.Write);
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

    private static byte[] DeriveKey(string keyString)
    {
        // Use SHA-256 to derive a 256-bit key from the configured secret
        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(keyString));
    }
}
