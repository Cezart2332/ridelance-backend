using System.Globalization;

namespace Application.Users.PhoneVerification;

/// <summary>
/// Aduce un număr de telefon românesc la forma internațională, <c>+407…</c>.
/// </summary>
/// <remarks>
/// Oamenii îl scriu cum le vine: cu spații, cu puncte, „07…", „+407…", „00407…". Furnizorul de
/// SMS acceptă o singură formă, iar numărul salvat pe cont e cel scris de om — deci conversia se
/// face la trimitere, într-un singur loc, și nu se atinge de ce a tastat utilizatorul.
/// </remarks>
public static class RomanianPhoneNumber
{
    /// <summary><see langword="null" /> dacă nu e un număr de mobil românesc.</summary>
    public static string? ToInternational(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string digits = new([.. raw.Where(char.IsAsciiDigit)]);

        // 00407… și 407… ajung amândouă la 7…, ca să rămână o singură verificare mai jos.
        if (digits.StartsWith("0040", StringComparison.Ordinal))
        {
            digits = digits[4..];
        }
        else if (digits.StartsWith("40", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        else if (digits.StartsWith('0'))
        {
            digits = digits[1..];
        }

        // Mobilele românești: 7 urmat de încă opt cifre.
        bool isMobile = digits.Length == 9 && digits[0] == '7';

        return isMobile ? string.Create(CultureInfo.InvariantCulture, $"+40{digits}") : null;
    }
}
