using System.Globalization;
using System.Text.RegularExpressions;
using Domain.PfaRegistrations;

namespace Domain.Documents;

/// <summary>
/// Validatoare deterministe pentru câmpurile extrase prin OCR. Încrederea auto-raportată de
/// model nu e de încredere singură, deci o combinăm cu aceste verificări:
/// <c>EffectiveConfidence = ValidatorPassed ? AiConfidence : min(AiConfidence, 0.30)</c>.
/// </summary>
public static class ExtractedFieldValidators
{
    private const double FailedConfidenceCap = 0.30;

    // 17 caractere, fără I/O/Q (standard ISO 3779).
    private static readonly Regex VinRegex = new(
        "^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Plăcuță RO: județ (1-2 litere, B pentru București) + 2-3 cifre + 3 litere.
    private static readonly Regex PlateRegex = new(
        "^(?:[A-Z]{1,2})(?:[0-9]{2,3})(?:[A-Z]{3})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Codul CAEN al transportului alternativ — „Alte servicii de transport terestru de călători
    /// n.c.a.". Fără el, PFA-ul nu poate factura curse pe Uber/Bolt.
    /// </summary>
    public const string RequiredCaen = "4939";

    // Modelul întoarce des codul lipit de denumire („4939 - Alte transporturi terestre de
    // călători n.c.a."), uneori mai multe coduri într-un singur text. Luăm grupurile de 4 cifre.
    private static readonly Regex CaenRegex = new(
        @"(?<!\d)\d{4}(?!\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Normalizează valoarea brută în funcție de tip (trim, uppercase, fără spații etc.).</summary>
    public static string? Normalize(ExtractedFieldType type, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string trimmed = raw.Trim();

        return type switch
        {
            ExtractedFieldType.Iban or ExtractedFieldType.Vin or ExtractedFieldType.Plate or ExtractedFieldType.Cui =>
                trimmed.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
            ExtractedFieldType.Caen => NormalizeCaen(trimmed),
            _ => trimmed,
        };
    }

    /// <summary>Codurile CAEN găsite în text, deduplicate, separate prin virgulă.</summary>
    private static string? NormalizeCaen(string raw)
    {
        string[] codes = CaenRegex.Matches(raw)
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return codes.Length == 0 ? null : string.Join(",", codes);
    }

    /// <summary>Rulează validatorul determinist pentru tipul câmpului pe valoarea deja normalizată.</summary>
    public static bool Validate(ExtractedFieldType type, string? normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return type switch
        {
            ExtractedFieldType.Cui => CuiValidator.Validate(normalized).IsValid,
            ExtractedFieldType.Iban => IsValidIban(normalized),
            ExtractedFieldType.Vin => VinRegex.IsMatch(normalized),
            ExtractedFieldType.Plate => PlateRegex.IsMatch(normalized),
            ExtractedFieldType.Date => IsSaneDate(normalized),
            // Certificatul poate lista mai multe coduri; ne interesează doar să apară cel cerut.
            ExtractedFieldType.Caen => normalized.Split(',').Contains(RequiredCaen, StringComparer.Ordinal),
            _ => true,
        };
    }

    public static double EffectiveConfidence(bool validatorPassed, double aiConfidence) =>
        validatorPassed ? aiConfidence : Math.Min(aiConfidence, FailedConfidenceCap);

    private static bool IsSaneDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
        && parsed.Year is >= 1900 and <= 2100;

    private static bool IsValidIban(string iban)
    {
        if (iban.Length is < 15 or > 34)
        {
            return false;
        }

        // Mut primele 4 caractere la coadă, convertesc literele în numere (A=10..Z=35), mod 97 == 1.
        string rearranged = string.Concat(iban.AsSpan(4), iban.AsSpan(0, 4));

        var digits = new System.Text.StringBuilder(rearranged.Length * 2);
        foreach (char c in rearranged)
        {
            if (char.IsAsciiLetterUpper(c))
            {
                digits.Append(c - 'A' + 10);
            }
            else if (char.IsAsciiDigit(c))
            {
                digits.Append(c);
            }
            else
            {
                return false;
            }
        }

        // Mod 97 pe bucăți, ca să evit numerele mari.
        int remainder = 0;
        foreach (char d in digits.ToString())
        {
            remainder = (remainder * 10 + d - '0') % 97;
        }

        return remainder == 1;
    }
}
