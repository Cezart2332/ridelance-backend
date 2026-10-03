using System.Globalization;
using System.Text.RegularExpressions;

namespace Application.Accounting.Spv;

/// <summary>Ce spune o recipisă SPV: declarația, perioada și numărul de înregistrare.</summary>
internal sealed record SpvRecipisaInfo(string DeclarationType, string? Period, string? RegistrationNumber);

/// <summary>
/// Detaliile unei recipise din <c>listaMesaje</c>, de forma
/// „recipisa pentru CIF 12345674, tip D100, numar_inregistrare INTERNT-…, perioada raportare 8.2026”.
/// Citirea e tolerantă (ordinea și spațiile variază); fără tipul declarației nu se leagă nimic.
/// </summary>
internal static partial class SpvRecipisa
{
    public static bool IsRecipisa(string type) =>
        type.Contains("RECIPIS", StringComparison.OrdinalIgnoreCase);

    /// <summary>Recipisa raportează erori (declarație respinsă la prelucrare).</summary>
    public static bool IsError(string? details) => details is not null && ErrorPattern().IsMatch(details);

    public static SpvRecipisaInfo? Read(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return null;
        }

        Match type = TypePattern().Match(details);
        if (!type.Success)
        {
            return null;
        }

        string? period = null;
        Match periodMatch = PeriodPattern().Match(details);
        if (periodMatch.Success &&
            int.TryParse(periodMatch.Groups["month"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int month) &&
            int.TryParse(periodMatch.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int year) &&
            month is >= 1 and <= 12)
        {
            period = string.Create(CultureInfo.InvariantCulture, $"{year:0000}-{month:00}");
        }

        Match number = NumberPattern().Match(details);
        return new SpvRecipisaInfo(type.Groups["type"].Value.ToUpperInvariant(), period, number.Success ? number.Groups["number"].Value : null);
    }

    [GeneratedRegex(@"\b(erori|eroare|respins[aă]?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(@"\btip\s*:?\s*(?<type>D\d{3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex TypePattern();

    [GeneratedRegex(@"perioad[aă]\s+(?:de\s+)?raportare\s*:?\s*(?<month>\d{1,2})[./-](?<year>\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodPattern();

    [GeneratedRegex(@"num[aă]r[_\s]inregistrare\s*:?\s*(?<number>[A-Za-z0-9][A-Za-z0-9\-/]*)", RegexOptions.IgnoreCase)]
    private static partial Regex NumberPattern();
}
