using System.Globalization;

namespace Domain.Documents;

/// <summary>Ce s-a citit din zona MRZ a unui act de identitate și dacă cifrele de control se verifică.</summary>
/// <param name="DocumentNumber">Seria și numărul actului, fără umplutura <c>&lt;</c> (ex. <c>RX123456</c>).</param>
/// <param name="ChecksValid">
/// Toate cifrele de control se verifică. Un act fabricat sau editat de mână rareori le nimerește:
/// sunt sume ponderate pe care cine modifică un nume sau o dată nu le recalculează.
/// </param>
public sealed record MrzReading(
    string Format,
    string DocumentNumber,
    string Surname,
    string GivenNames,
    DateOnly? BirthDate,
    DateOnly? ExpiryDate,
    bool ChecksValid);

/// <summary>
/// Zona citibilă automat (MRZ) de pe actele de identitate, după ICAO 9303.
///
/// Cartea de identitate electronică are formatul TD1 (trei rânduri de 30 de caractere, pe verso),
/// cea clasică un format pe două rânduri de 36 (TD2, pe față). Funcție pură: primește rândurile
/// așa cum le-a transcris modelul, nu face niciun apel.
/// </summary>
public static class MrzValidator
{
    private static readonly int[] Weights = [7, 3, 1];

    /// <summary>
    /// Parsează MRZ-ul. Null când textul nu are forma unui MRZ — rânduri lipsă, lungimi greșite —,
    /// adică nu s-a putut citi; nu înseamnă că actul e fals.
    /// </summary>
    public static MrzReading? Parse(string? raw, DateOnly today)
    {
        List<string> lines = SplitLines(raw);

        if (lines.Count == 3 && lines.TrueForAll(l => l.Length == 30))
        {
            return ParseTd1(lines, today);
        }

        if (lines.Count == 2 && lines.TrueForAll(l => l.Length == 36))
        {
            return ParseTd2(lines, today);
        }

        return null;
    }

    /// <summary>Cifra de control ICAO: sumă ponderată 7-3-1, modulo 10.</summary>
    public static int CheckDigit(string value)
    {
        int sum = 0;
        for (int i = 0; i < value.Length; i++)
        {
            sum += CharValue(value[i]) * Weights[i % 3];
        }

        return sum % 10;
    }

    private static MrzReading ParseTd1(List<string> lines, DateOnly today)
    {
        string l1 = lines[0];
        string l2 = lines[1];
        string l3 = lines[2];

        string number = l1[5..14];
        string birth = l2[..6];
        string expiry = l2[8..14];

        bool valid =
            Matches(number, l1[14]) &&
            Matches(birth, l2[6]) &&
            Matches(expiry, l2[14]) &&
            Matches(l1[5..30] + l2[..7] + l2[8..15] + l2[18..29], l2[29]);

        (string surname, string given) = SplitNames(l3);

        return new MrzReading(
            "TD1",
            Clean(number),
            surname,
            given,
            ParseBirth(birth, today),
            ParseExpiry(expiry),
            valid);
    }

    private static MrzReading ParseTd2(List<string> lines, DateOnly today)
    {
        string l1 = lines[0];
        string l2 = lines[1];

        string number = l2[..9];
        string birth = l2[13..19];
        string expiry = l2[21..27];

        bool valid =
            Matches(number, l2[9]) &&
            Matches(birth, l2[19]) &&
            Matches(expiry, l2[27]) &&
            Matches(l2[..10] + l2[13..20] + l2[21..35], l2[35]);

        (string surname, string given) = SplitNames(l1[5..]);

        return new MrzReading(
            "TD2",
            Clean(number),
            surname,
            given,
            ParseBirth(birth, today),
            ParseExpiry(expiry),
            valid);
    }

    /// <summary>
    /// Rândurile MRZ, curățate de ce adaugă transcrierea: spații, ghilimele unghiulare în loc de
    /// <c>&lt;</c>, litere mici. Acceptă și MRZ-ul lipit într-un singur rând, dacă lungimea îl trădează.
    /// </summary>
    private static List<string> SplitLines(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        string normalized = raw
            .Replace('«', '<')
            .Replace('‹', '<')
            .ToUpperInvariant();

        var lines = normalized
            .Split(['\n', '\r', '|', ';', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => new string(l.Where(c => c != ' ' && c != '\t').ToArray()))
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 1)
        {
            string joined = lines[0];
            if (joined.Length == 90)
            {
                return [joined[..30], joined[30..60], joined[60..]];
            }

            if (joined.Length == 72)
            {
                return [joined[..36], joined[36..]];
            }
        }

        return lines;
    }

    private static (string Surname, string Given) SplitNames(string field)
    {
        int separator = field.IndexOf("<<", StringComparison.Ordinal);
        if (separator < 0)
        {
            return (Clean(field), string.Empty);
        }

        return (Clean(field[..separator]), Clean(field[(separator + 2)..]));
    }

    private static string Clean(string value) =>
        string.Join(' ', value.Split('<', StringSplitOptions.RemoveEmptyEntries));

    private static bool Matches(string value, char check) =>
        char.IsAsciiDigit(check) && CheckDigit(value) == check - '0';

    private static int CharValue(char c) => c switch
    {
        '<' => 0,
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'Z' => c - 'A' + 10,
        _ => 0,
    };

    /// <summary>Anul are două cifre: nașterea e cea mai recentă variantă care nu e în viitor.</summary>
    private static DateOnly? ParseBirth(string yymmdd, DateOnly today)
    {
        DateOnly? candidate = ParseYyMmDd(yymmdd, 2000);
        if (candidate is null)
        {
            return null;
        }

        return candidate.Value > today ? candidate.Value.AddYears(-100) : candidate;
    }

    /// <summary>Actele românești în circulație expiră în secolul ăsta.</summary>
    private static DateOnly? ParseExpiry(string yymmdd) => ParseYyMmDd(yymmdd, 2000);

    private static DateOnly? ParseYyMmDd(string yymmdd, int century) =>
        int.TryParse(yymmdd.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int yy) &&
        int.TryParse(yymmdd.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int mm) &&
        int.TryParse(yymmdd.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int dd) &&
        mm is >= 1 and <= 12 &&
        dd >= 1 && dd <= DateTime.DaysInMonth(century + yy, mm)
            ? new DateOnly(century + yy, mm, dd)
            : null;
}
