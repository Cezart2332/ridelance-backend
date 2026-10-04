using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Domain.PfaRegistrations;

namespace Application.Accounting.Documents;

/// <summary>Un client PFA, cum îl caută încărcarea globală: CUI-ul și numele.</summary>
public sealed record InboxClient(Guid PfaId, string? Cui, IReadOnlyList<string> Names);

/// <summary>
/// Potrivirea unui document încărcat global cu clientul (fără AI): CUI-ul din text, apoi CUI-ul sau numele
/// din numele fișierului. Funcții pure; potrivirea după comision vine după citire.
/// </summary>
public static partial class PlatformInboxMatcher
{
    /// <summary>Cuvintele care nu deosebesc un client de altul.</summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "PFA", "II", "IF", "SRL", "PERSOANA", "FIZICA", "AUTORIZATA", "INTREPRINDERE", "INDIVIDUALA", "RAPORT", "FACTURA", "BOLT", "UBER",
    };

    /// <summary>CUI-ul fără „RO” și fără zerouri în față.</summary>
    public static string? NormalizeCui(string? cui)
    {
        if (string.IsNullOrWhiteSpace(cui))
        {
            return null;
        }

        string digits = new([.. cui.Where(char.IsDigit)]);
        digits = digits.TrimStart('0');
        return digits.Length >= 2 ? digits : null;
    }

    /// <summary>Toate numerele cu cifra de control corectă de CUI (6–10 cifre), din text.</summary>
    public static IReadOnlyList<string> ValidCuisIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return [.. NumberPattern().Matches(text)
            .Select(match => match.Groups["cui"].Value)
            .Where(value => CuiValidator.Validate(value).IsValid)
            .Select(value => NormalizeCui(value)!)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>CUI-urile marcate ca atare („CUI”, „CIF”, „Cod fiscal”, „VAT”), cu cifra de control corectă.</summary>
    public static IReadOnlyList<string> LabelledCuisIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return [.. LabelPattern().Matches(text)
            .Select(match => match.Groups["cui"].Value)
            .Where(value => CuiValidator.Validate(value).IsValid)
            .Select(value => NormalizeCui(value)!)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Clientul al cărui CUI apare în text; <c>null</c> dacă niciunul sau mai mulți.</summary>
    public static InboxClient? ByCui(string? text, IReadOnlyList<InboxClient> clients)
    {
        HashSet<string> found = [.. ValidCuisIn(text)];
        List<InboxClient> matches = [.. clients.Where(c => NormalizeCui(c.Cui) is { } cui && found.Contains(cui))];
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Clientul din numele fișierului: CUI-ul lui sau toate cuvintele numelui; doar dacă e unic.</summary>
    public static InboxClient? ByFileName(string fileName, IReadOnlyList<InboxClient> clients)
    {
        string name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        if (ByCui(name, clients) is { } byCui)
        {
            return byCui;
        }

        return ByName(name, clients);
    }

    /// <summary>Clientul ale cărui cuvinte din nume (cel puțin două) apar toate în text; doar dacă e unic.</summary>
    public static InboxClient? ByName(string? text, IReadOnlyList<InboxClient> clients)
    {
        HashSet<string> words = [.. Words(text)];
        if (words.Count == 0)
        {
            return null;
        }

        List<InboxClient> matches = [.. clients.Where(client => client.Names.Any(full =>
        {
            List<string> parts = [.. Words(full).Where(word => word.Length > 2 && !Common.Contains(word))];
            return parts.Count >= 2 && parts.All(words.Contains);
        }))];
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Luna din numele fișierului (<c>2026-08</c>, <c>08.2026</c>, <c>08_2026</c>), dacă e scrisă.</summary>
    public static string? PeriodFromFileName(string fileName)
    {
        Match match = PeriodPattern().Match(fileName ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        string year = match.Groups["y1"].Success ? match.Groups["y1"].Value : match.Groups["y2"].Value;
        string month = match.Groups["m1"].Success ? match.Groups["m1"].Value : match.Groups["m2"].Value;
        return int.Parse(month, CultureInfo.InvariantCulture) is >= 1 and <= 12 ? $"{year}-{month}" : null;
    }

    /// <summary>Cuvintele unui nume: majuscule, fără diacritice, fără semne.</summary>
    public static IEnumerable<string> Words(string? value)
    {
        string decomposed = (value ?? string.Empty).ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }
        }

        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    [GeneratedRegex(@"(?<![\d.,])(?:RO\s?)?(?<cui>\d{6,10})(?![\d.,]\d)", RegexOptions.IgnoreCase, 500)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\b(?:C\.?U\.?I\.?|C\.?I\.?F\.?|cod\s+fiscal|cod\s+de\s+identificare\s+fiscal[aă]|tax\s*id|vat\s*(?:no\.?|number|id)?|nr\.?\s*TVA)\b[^\d\n]{0,20}(?:RO\s?)?(?<cui>\d{2,10})(?!\d)", RegexOptions.IgnoreCase, 500)]
    private static partial Regex LabelPattern();

    [GeneratedRegex(@"(?<!\d)(?:(?<y1>20\d{2})[-_.](?<m1>\d{2})|(?<m2>\d{2})[-_.](?<y2>20\d{2}))(?!\d)", RegexOptions.None, 500)]
    private static partial Regex PeriodPattern();
}
