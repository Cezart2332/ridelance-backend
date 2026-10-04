using System.Security.Cryptography;
using System.Text;

namespace Application.Users.TwoFactor;

/// <summary>
/// Coduri TOTP (RFC 6238): HMAC-SHA1, pași de 30 s, 6 cifre — ce citesc Authy, Google
/// Authenticator, Microsoft Authenticator, 1Password. Funcții pure, fără stare.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;

    /// <summary>Câți pași înainte și înapoi se acceptă, pentru ceasul telefonului.</summary>
    public const int Drift = 1;

    /// <summary>Un secret nou de 160 de biți, în Base32 (forma din codul QR).</summary>
    public static string NewSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    public static long StepAt(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds() / StepSeconds;

    public static string CodeAt(string base32Secret, long step)
    {
        byte[] counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

#pragma warning disable CA5350 // HMAC-SHA1 e algoritmul cerut de RFC 6238 și de aplicațiile de autentificare.
        byte[] hash = HMACSHA1.HashData(Base32.Decode(base32Secret), counter);
#pragma warning restore CA5350
        int offset = hash[^1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Pasul la care se potrivește codul, în fereastra ±<see cref="Drift"/>, sau <c>null</c>. Un pas
    /// mai vechi sau egal cu <paramref name="lastUsedStep"/> e refuzat: același cod nu trece de două ori.
    /// </summary>
    public static long? Match(string base32Secret, string? code, DateTime nowUtc, long? lastUsedStep)
    {
        string digits = new([.. (code ?? string.Empty).Where(char.IsDigit)]);
        if (digits.Length != Digits)
        {
            return null;
        }

        long now = StepAt(nowUtc);
        for (long step = now - Drift; step <= now + Drift; step++)
        {
            if (lastUsedStep is { } last && step <= last)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(CodeAt(base32Secret, step)), Encoding.ASCII.GetBytes(digits)))
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>Adresa din codul QR: <c>otpauth://totp/RIDElance:email?secret=…&amp;issuer=RIDElance</c>.</summary>
    public static string ProvisioningText(string issuer, string account, string base32Secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}

/// <summary>Base32 (RFC 4648), fără padding: forma secretelor TOTP.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(byte[] data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bits = 0;
        foreach (byte value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return output.ToString();
    }

    public static byte[] Decode(string text)
    {
        string clean = text.Trim().TrimEnd('=').Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0;
        int bits = 0;
        foreach (char ch in clean)
        {
            int index = Alphabet.IndexOf(ch, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new FormatException("Secret Base32 invalid.");
            }

            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
