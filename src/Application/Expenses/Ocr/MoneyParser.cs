using System.Globalization;
using System.Text.RegularExpressions;

namespace Application.Expenses.Ocr;

/// <summary>
/// Transformă în număr ce a citit OCR-ul de pe un bon.
///
/// Modelul întoarce string-uri brute, exact cum apar pe hârtie, iar hârtia din România nu are
/// un singur format: „1.234,56", „1,234.56", „1234,56 LEI", „284.00". Ambiguitatea reală e
/// separatorul — o virgulă poate fi zecimală sau de mii. Se decide după ce urmează:
/// exact trei cifre după ultimul separator, și niciun alt separator de tip diferit, înseamnă
/// separator de mii; altfel e zecimal.
///
/// Ce nu face: nu ghicește. O valoare pe care nu o poate citi cu certitudine devine null și
/// ajunge în formular ca un câmp gol de completat, nu ca o sumă inventată.
/// </summary>
public static class MoneyParser
{
    /// <summary>Peste atâția lei, o sumă citită de pe un bon e o greșeală de citire.</summary>
    private const decimal SanityCeiling = 1_000_000m;

    private static readonly Regex Cleanup = new(@"[^\d.,\-]", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static decimal? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Scoate „LEI", „RON", spații, simboluri — rămân doar cifre, separatori și semnul.
        string cleaned = Cleanup.Replace(raw, string.Empty);
        if (cleaned.Length == 0)
        {
            return null;
        }

        bool negative = cleaned.StartsWith('-');
        cleaned = cleaned.TrimStart('-');

        string normalized = Normalize(cleaned);
        if (normalized is null)
        {
            return null;
        }

        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
        {
            return null;
        }

        if (value > SanityCeiling)
        {
            return null;
        }

        decimal signed = negative ? -value : value;
        return Math.Round(signed, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Aduce numărul la forma invariantă `1234.56`, sau null dacă e ambiguu.</summary>
    private static string Normalize(string cleaned)
    {
        int lastComma = cleaned.LastIndexOf(',');
        int lastDot = cleaned.LastIndexOf('.');

        if (lastComma < 0 && lastDot < 0)
        {
            return cleaned;
        }

        // Ambii separatori prezenți: ultimul e cel zecimal, celălalt grupează miile.
        if (lastComma >= 0 && lastDot >= 0)
        {
            char decimalSeparator = lastComma > lastDot ? ',' : '.';
            char groupSeparator = decimalSeparator == ',' ? '.' : ',';
            return cleaned.Replace(groupSeparator.ToString(), string.Empty, StringComparison.Ordinal)
                .Replace(decimalSeparator, '.');
        }

        char separator = lastComma >= 0 ? ',' : '.';
        int lastIndex = Math.Max(lastComma, lastDot);
        int digitsAfter = cleaned.Length - lastIndex - 1;
        int occurrences = cleaned.Count(c => c == separator);

        // Exact trei cifre după separator: „1.234" e o mie două sute treizeci și patru, nu
        // 1 leu și 234 de bani. Cu mai multe apariții („1.234.567") e sigur grupare.
        if (digitsAfter == 3 && (occurrences > 1 || lastIndex > 0))
        {
            return cleaned.Replace(separator.ToString(), string.Empty, StringComparison.Ordinal);
        }

        // Mai mulți separatori de același fel, dar nu grupare validă — citire stricată.
        if (occurrences > 1)
        {
            return null;
        }

        return cleaned.Replace(separator, '.');
    }

    /// <summary>
    /// TVA-ul nu poate depăși totalul. O extragere care spune altfel a citit greșit unul
    /// dintre cele două; se renunță la TVA, nu la cheltuială.
    /// </summary>
    public static bool IsVatPlausible(decimal? total, decimal? vat) =>
        total is null || vat is null || vat >= 0 && vat <= total;
}
