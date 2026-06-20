using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Security;
using Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Infrastructure.Security;

internal sealed class SecretProtector(IOptions<EncryptionSettings> encryptionSettings) : ISecretProtector
{
    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        byte[] key = DeriveKey(encryptionSettings.Value.Key);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plaintext = Encoding.UTF8.GetBytes(plainText);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        using var aesGcm = new AesGcm(key, 16);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);

        return Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return string.Empty;
        }

        byte[] payload = Convert.FromBase64String(protectedText);
        byte[] nonce = payload[..12];
        byte[] tag = payload[12..28];
        byte[] ciphertext = payload[28..];
        byte[] plaintext = new byte[ciphertext.Length];
        byte[] key = DeriveKey(encryptionSettings.Value.Key);

        using var aesGcm = new AesGcm(key, 16);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DeriveKey(string keyString) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
}
