namespace Application.Cars;

/// <summary>
/// Tipul real al unei poze, citit din primii octeți, nu din numele fișierului.
///
/// Extensia venea din numele trimis de telefon, iar conținutul era altul: blurarea numărului
/// scoate mereu JPEG, deci un „.WEBP” ajungea cu JPEG înăuntru, iar un WebP redenumit „.jpg”
/// rămânea WebP. Serverul trimite `Content-Type` după extensie, și pe iPhone poza nu apărea.
/// </summary>
public static class CarPhotoFormat
{
    /// <summary>Extensia potrivită conținutului (".jpg", ".png", ".webp") sau null dacă nu e o poză cunoscută.</summary>
    public static string? ExtensionOf(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ".png";
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return null;
    }

    /// <summary>Extensia din numele fișierului se potrivește cu ce e înăuntru.</summary>
    public static bool ExtensionMatches(string fileName, ReadOnlySpan<byte> bytes)
    {
        string? actual = ExtensionOf(bytes);
        string declared = Path.GetExtension(fileName).ToUpperInvariant();
        return actual switch
        {
            ".jpg" => declared is ".JPG" or ".JPEG",
            null => true,
            _ => string.Equals(declared, actual, StringComparison.OrdinalIgnoreCase),
        };
    }
}
