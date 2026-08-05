using System.Globalization;

namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>
/// Validarea CNP-ului: 13 cifre cu cifră de control, plus datele derivate din el
/// (data nașterii și sexul), folosite pentru verificarea de consistență cu CI-ul.
/// </summary>
public static class CnpValidator
{
    /// <summary>Ponderile standard pentru cifra de control.</summary>
    private static readonly int[] Weights = [2, 7, 9, 1, 4, 6, 3, 5, 8, 2, 7, 9];

    /// <summary>Secolul nașterii, după prima cifră (S) din CNP.</summary>
    private static int? CenturyOf(int s) => s switch
    {
        1 or 2 => 1900,
        3 or 4 => 1800,
        5 or 6 => 2000,
        // 7/8/9 = rezidenți străini; anul se deduce tot din 1900+, dar nu garantat.
        7 or 8 or 9 => 1900,
        _ => null,
    };

    /// <summary>CNP-ul are 13 cifre și cifra de control corectă.</summary>
    public static bool IsValid(string? cnp)
    {
        if (string.IsNullOrWhiteSpace(cnp))
        {
            return false;
        }

        string digits = cnp.Trim();
        if (digits.Length != 13 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            sum += (digits[i] - '0') * Weights[i];
        }

        int remainder = sum % 11;
        int checkDigit = remainder == 10 ? 1 : remainder;

        return checkDigit == digits[12] - '0' && BirthDateOf(digits) is not null;
    }

    /// <summary>Data nașterii codificată în CNP, sau null dacă e imposibilă.</summary>
    public static DateOnly? BirthDateOf(string? cnp)
    {
        if (string.IsNullOrWhiteSpace(cnp) || cnp.Length != 13 || !cnp.All(char.IsAsciiDigit))
        {
            return null;
        }

        int century = CenturyOf(cnp[0] - '0') ?? 0;
        if (century == 0)
        {
            return null;
        }

        int year = century + int.Parse(cnp.AsSpan(1, 2), CultureInfo.InvariantCulture);
        int month = int.Parse(cnp.AsSpan(3, 2), CultureInfo.InvariantCulture);
        int day = int.Parse(cnp.AsSpan(5, 2), CultureInfo.InvariantCulture);

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        return new DateOnly(year, month, day);
    }

    /// <summary>„M" / „F" după prima cifră, sau null dacă nu se poate determina.</summary>
    public static string? SexOf(string? cnp)
    {
        if (string.IsNullOrWhiteSpace(cnp) || cnp.Length != 13 || !char.IsAsciiDigit(cnp[0]))
        {
            return null;
        }

        return (cnp[0] - '0') switch
        {
            1 or 3 or 5 or 7 => "M",
            2 or 4 or 6 or 8 => "F",
            _ => null,
        };
    }

    /// <summary>Masca de afișare: prima cifră, șase asteriscuri, ultimele șase cifre.</summary>
    public static string Mask(string? cnp) =>
        string.IsNullOrWhiteSpace(cnp) || cnp.Length != 13
            ? "•••••••••••••"
            : $"{cnp[0]}******{cnp[^6..]}";
}
