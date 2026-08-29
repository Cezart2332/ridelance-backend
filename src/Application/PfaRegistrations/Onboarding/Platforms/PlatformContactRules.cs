using System.Text.RegularExpressions;

namespace Application.PfaRegistrations.Onboarding.Platforms;

/// <summary>
/// Regulile de formă pentru datele de contact ale conturilor de platformă.
///
/// Telefonul se normalizează la E.164 înainte de validare, nu doar se respinge: șoferii îl
/// tastează cum îl știu — „0712 345 678", „0040712345678", „+40 712 345 678" — și toate trei
/// sunt același număr. Ce ajunge în DB e mereu forma canonică, fiindcă platformele o cer așa.
/// </summary>
internal static partial class PlatformContactRules
{
    /// <summary>Prefixul implicit: dosarele sunt românești, iar „07…" nu e ambiguu aici.</summary>
    private const string DefaultCountryCode = "40";

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
    private static partial Regex EmailPattern();

    /// <summary>Email plauzibil sintactic. Confirmarea reală o face platforma, nu noi.</summary>
    public static bool IsValidEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && EmailPattern().IsMatch(value.Trim());

    /// <summary>
    /// Forma E.164 a numărului, sau null dacă nu se poate deduce una.
    ///
    /// Acceptă: „+40712345678", „0040712345678", „0712345678" și oricare din ele cu spații,
    /// puncte sau cratime.
    /// </summary>
    public static string? ToE164(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string raw = value.Trim();
        bool hadPlus = raw.StartsWith('+');
        string digits = new([.. raw.Where(char.IsAsciiDigit)]);

        if (digits.Length == 0)
        {
            return null;
        }

        if (!hadPlus)
        {
            if (digits.StartsWith("00", StringComparison.Ordinal))
            {
                digits = digits[2..];
            }
            else if (digits.StartsWith('0'))
            {
                // Număr local: „0712345678" → „40712345678".
                digits = DefaultCountryCode + digits[1..];
            }
        }

        // E.164: maximum 15 cifre, iar sub 8 nu există număr național real.
        return digits.Length is >= 8 and <= 15 ? "+" + digits : null;
    }

    public static bool IsValidPhone(string? value) => ToE164(value) is not null;
}
