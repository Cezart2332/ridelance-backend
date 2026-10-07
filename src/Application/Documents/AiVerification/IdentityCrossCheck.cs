using System.Globalization;
using System.Text;
using Domain.Documents;

namespace Application.Documents.AiVerification;

/// <summary>
/// Cine apare pe un document, așa cum s-a citit. Orice câmp poate lipsi; comparația folosește doar
/// ce există pe ambele părți.
/// </summary>
public sealed record IdentityFacts(
    string? Surname = null,
    string? GivenNames = null,
    string? FullName = null,
    string? Cnp = null,
    DateOnly? BirthDate = null,
    string? DocumentNumber = null,
    DateOnly? ExpiresOn = null)
{
    /// <summary>Nu e nimic de comparat: niciun nume, CNP sau dată.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Surname) && string.IsNullOrWhiteSpace(GivenNames) &&
        string.IsNullOrWhiteSpace(FullName) && string.IsNullOrWhiteSpace(Cnp) &&
        BirthDate is null && string.IsNullOrWhiteSpace(DocumentNumber) && ExpiresOn is null;

    /// <summary>
    /// Faptele din câmpurile extrase ale unui document. Cheile sunt cele din catalog: buletinul are
    /// <c>nume</c>/<c>prenume</c>/<c>cnp</c>, permisul <c>titular_nume</c>/<c>titular_prenume</c>,
    /// restul actelor personale <c>titular</c>/<c>cnp_titular</c>, certificatul PFA <c>holder_name</c>.
    /// </summary>
    public static IdentityFacts From(IReadOnlyDictionary<string, string> fields)
    {
        string? Get(params string[] keys) =>
            keys.Select(k => fields.TryGetValue(k, out string? v) ? v : null)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        string? series = Get("serie_act");
        string? number = Get("numar_act");

        return new IdentityFacts(
            Surname: Get("nume", "titular_nume"),
            GivenNames: Get("prenume", "titular_prenume"),
            FullName: Get("full_name", "titular", "holder_name"),
            Cnp: Get("cnp", "cnp_titular"),
            BirthDate: DocumentDateValidator.Parse(Get("date_of_birth", "titular_data_nasterii")),
            DocumentNumber: number is null ? null : $"{series}{number}",
            ExpiresOn: DocumentDateValidator.Parse(Get("data_expirarii")));
    }

    public static IdentityFacts From(MrzReading mrz) =>
        new(mrz.Surname, mrz.GivenNames, null, null, mrz.BirthDate, mrz.DocumentNumber, mrz.ExpiryDate);
}

/// <summary>
/// Dacă un document e al aceleiași persoane ca buletinul. Funcție pură: primește ce s-a citit de pe
/// ambele, întoarce nepotrivirile ca motive scurte, în română.
///
/// Numele se compară tolerant — fără diacritice, în orice ordine, cu prenumele doar parțial
/// (permisul poate avea un singur prenume din două) —, fiindcă OCR-ul și actele diferă la forme.
/// CNP-ul și data nașterii se compară exact: acolo o diferență chiar înseamnă altă persoană.
/// </summary>
public static class IdentityCrossCheck
{
    private const string AgainstIdCard = "din buletin";
    private const string Differs = "diferă";

    /// <summary>
    /// Motivul vine de aici, nu din verificarea de autenticitate. Când potrivirea reușește mai
    /// târziu, doar motivele acestea se șterg; celelalte rămân pentru admin.
    /// </summary>
    public static bool IsIdentityReason(string reason) =>
        reason.Contains(AgainstIdCard, StringComparison.Ordinal) ||
        reason.Contains(Differs, StringComparison.Ordinal);

    public static IReadOnlyList<string> Mismatches(
        IdentityFacts reference,
        IdentityFacts candidate,
        string documentLabel,
        bool compareDocumentNumbers = false)
    {
        var reasons = new List<string>();

        string? refCnp = Digits(reference.Cnp);
        string? candCnp = Digits(candidate.Cnp);
        if (refCnp is { Length: 13 } && candCnp is { Length: 13 } && refCnp != candCnp)
        {
            reasons.Add($"CNP-ul de pe „{documentLabel}” nu e cel din buletin.");
        }

        if (NamesConflict(reference, candidate))
        {
            reasons.Add($"Numele de pe „{documentLabel}” nu e cel din buletin.");
        }

        if (reference.BirthDate is DateOnly refBirth && candidate.BirthDate is DateOnly candBirth && refBirth != candBirth)
        {
            reasons.Add($"Data nașterii de pe „{documentLabel}” nu e cea din buletin.");
        }

        if (compareDocumentNumbers)
        {
            string? refNumber = Alnum(reference.DocumentNumber);
            string? candNumber = Alnum(candidate.DocumentNumber);
            if (refNumber is not null && candNumber is not null && refNumber != candNumber)
            {
                reasons.Add($"Seria și numărul de pe „{documentLabel}” diferă.");
            }

            if (reference.ExpiresOn is DateOnly refExp && candidate.ExpiresOn is DateOnly candExp && refExp != candExp)
            {
                reasons.Add($"Data expirării de pe „{documentLabel}” diferă.");
            }
        }

        return reasons;
    }

    /// <summary>
    /// Numele se contrazic. Cu numele de familie și prenumele separate pe referință, candidatul
    /// trebuie să conțină măcar un cuvânt din fiecare. Cu un singur nume complet, trebuie să aibă
    /// două cuvinte comune (sau toate, dacă unul are doar unul). Fără nume pe una din părți: nimic.
    /// </summary>
    internal static bool NamesConflict(IdentityFacts reference, IdentityFacts candidate)
    {
        HashSet<string> candidateTokens = Tokens(candidate.Surname, candidate.GivenNames, candidate.FullName);
        if (candidateTokens.Count == 0)
        {
            return false;
        }

        HashSet<string> surname = Tokens(reference.Surname);
        HashSet<string> given = Tokens(reference.GivenNames);

        if (surname.Count > 0 && given.Count > 0)
        {
            return !candidateTokens.Overlaps(surname) || !candidateTokens.Overlaps(given);
        }

        HashSet<string> all = Tokens(reference.Surname, reference.GivenNames, reference.FullName);
        if (all.Count == 0)
        {
            return false;
        }

        int common = all.Count(candidateTokens.Contains);
        int needed = Math.Min(2, Math.Min(all.Count, candidateTokens.Count));
        return common < needed;
    }

    /// <summary>Cuvintele unui nume: majuscule, fără diacritice, cratimele ca separator.</summary>
    internal static HashSet<string> Tokens(params string?[] parts)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            var current = new StringBuilder();
            foreach (char c in RemoveDiacritics(part).ToUpperInvariant())
            {
                if (char.IsLetter(c))
                {
                    current.Append(c);
                    continue;
                }

                Flush(current, tokens);
            }

            Flush(current, tokens);
        }

        return tokens;
    }

    private static void Flush(StringBuilder current, HashSet<string> tokens)
    {
        // Inițialele („I.”) nu identifică pe nimeni.
        if (current.Length >= 2)
        {
            tokens.Add(current.ToString());
        }

        current.Clear();
    }

    private static string RemoveDiacritics(string value)
    {
        string decomposed = value
            .Replace('ş', 's').Replace('Ş', 'S')
            .Replace('ţ', 't').Replace('Ţ', 'T')
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string? Digits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string digits = new(value.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    private static string? Alnum(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string cleaned = new(value.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return cleaned.Length == 0 ? null : cleaned;
    }
}
