using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Application.Cars;

/// <summary>
/// Cine a văzut anunțul, fără să știm cine e.
///
/// Adresa IP nu se stochează: din ea, plus user-agent și un salt de configurare, iese un hash de
/// 64 de caractere. E suficient ca să recunoaștem același vizitator timp de 30 de minute și prea
/// puțin ca să însemne o identitate — saltul face imposibilă și verificarea unui IP ghicit.
/// </summary>
public static class VisitorFingerprint
{
    public static string Compute(string? ipAddress, string? userAgent, string salt)
    {
        string material = string.Join(
            '|',
            ipAddress ?? "unknown",
            userAgent ?? "unknown",
            salt);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));

        var builder = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
