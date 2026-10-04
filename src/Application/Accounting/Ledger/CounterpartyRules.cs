using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Domain.Accounting;

namespace Application.Accounting.Ledger;

/// <summary>Ce este o tranzacție bancară după contrapartida ei (spec flux contabil §6).</summary>
public enum CounterpartyKind
{
    /// <summary>Nimic special: plată către furnizor sau încasare de identificat.</summary>
    None = 0,

    /// <summary>Payout Uber/Bolt (R20): nu e venit până la reconcilierea cu raportul.</summary>
    PlatformSettlement = 1,

    /// <summary>PFA → contul personal al titularului (R40): utilizare venit, nu cheltuială.</summary>
    OwnerWithdrawal = 2,

    /// <summary>Titular → PFA (R41): aport, nu venit.</summary>
    OwnerContribution = 3,

    /// <summary>Plată către ANAF / Trezorerie (R42).</summary>
    Tax = 4,

    /// <summary>Între conturile PFA-ului (R43): fără efect fiscal.</summary>
    InternalTransfer = 5,

    /// <summary>Comisionul băncii pentru administrarea contului („plan fee”).</summary>
    BankFee = 6,
}

/// <summary>Cine e PFA-ul pentru banca lui: numele titularului și conturile proprii.</summary>
/// <param name="OwnerNames">Numele titularului, cum pot apărea ca contrapartidă (Nume Prenume, în orice ordine).</param>
/// <param name="OwnIbans">IBAN-urile conturilor PFA (<c>BankAccount.Iban</c>), normalizate.</param>
public sealed record PfaIdentity(IReadOnlyList<string> OwnerNames, IReadOnlySet<string> OwnIbans);

/// <summary>
/// Clasificarea după contrapartidă, înainte de categoria cheltuielii. Funcție pură, pe date
/// normalizate (§5 pas 2): majuscule, fără diacritice, fără forme juridice.
/// </summary>
public static class CounterpartyRules
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Cuvintele care marchează un cont al PFA-ului, nu al persoanei.</summary>
    private static readonly string[] PfaMarkers = ["PFA", "PERSOANA FIZICA AUTORIZATA", "II", "INTREPRINDERE INDIVIDUALA"];

    /// <summary>Cuvintele dinaintea numelui în detaliile unui transfer („To Victor Ionescu”, „De la …”).</summary>
    private static readonly HashSet<string> TransferWords = new(StringComparer.Ordinal)
    {
        "TO", "FROM", "CATRE", "DE", "LA", "TRANSFER", "PLATA", "P2P", "SENT", "RECEIVED", "MONEY", "PAYMENT",
    };

    private static readonly HashSet<string> LegalForms = new(StringComparer.Ordinal)
    {
        "SRL", "SA", "SRL-D", "SCS", "SNC", "PFA", "II", "IF", "OU", "BV", "LTD", "GMBH", "INC", "LLC", "B.V.",
    };

    public static CounterpartyKind Classify(
        decimal amount,
        string? counterpartyName,
        string? counterpartyIban,
        string? remittance,
        PfaIdentity identity,
        AccountingOptions options)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);

        // R43: IBAN-ul contrapartidei e al unui cont al PFA-ului. Conturile salvate înainte, cu IBAN-ul
        // mascat („RO49••••0002”), se compară după mască până la recuperarea IBAN-ului complet.
        if (NormalizeIban(counterpartyIban) is { } iban &&
            (identity.OwnIbans.Contains(iban) || identity.OwnIbans.Contains(MaskIban(iban)!)))
        {
            return CounterpartyKind.InternalTransfer;
        }

        string text = string.Join(' ', new[] { counterpartyName, remittance }.Where(t => !string.IsNullOrWhiteSpace(t)));

        // R42: Trezoreria are IBAN-uri RO..TREZ..; numele și detaliile acoperă restul.
        if (amount < 0 &&
            ((counterpartyIban?.Contains("TREZ", StringComparison.OrdinalIgnoreCase) ?? false) ||
             text.Length > 0 && Regex.IsMatch(Normalize(text), options.TaxCounterpartyPattern, RegexOptions.CultureInvariant, PatternTimeout)))
        {
            return CounterpartyKind.Tax;
        }

        // R20: payout-ul platformei.
        if (amount > 0 && PlatformPayouts.PlatformOf(options, counterpartyName, remittance) is not null)
        {
            return CounterpartyKind.PlatformSettlement;
        }

        // R40 / R41: titularul, ca persoană. Același nume cu marcaj de PFA e alt cont al PFA-ului (R43).
        // Fără nume de contrapartidă, numele se caută în detalii („To Victor Ionescu”, „From Victor I”).
        string? owner = string.IsNullOrWhiteSpace(counterpartyName) ? remittance : counterpartyName;
        if (IsOwner(owner, identity.OwnerNames))
        {
            if (HasPfaMarker(owner))
            {
                return CounterpartyKind.InternalTransfer;
            }

            return amount < 0 ? CounterpartyKind.OwnerWithdrawal : CounterpartyKind.OwnerContribution;
        }

        // Comisionul de administrare al băncii.
        if (amount < 0 && text.Length > 0 &&
            Regex.IsMatch(Normalize(text), options.BankFeePattern, RegexOptions.CultureInvariant, PatternTimeout))
        {
            return CounterpartyKind.BankFee;
        }

        return CounterpartyKind.None;
    }

    /// <summary>IBAN de Trezorerie (<c>RO..TREZ..</c>): plata e sigur la buget, nu doar după nume.</summary>
    public static bool IsTreasuryIban(string? iban) => iban?.Contains("TREZ", StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>
    /// Plata e sigur la buget: IBAN de Trezorerie sau contrapartida e chiar Trezoreria / ANAF. Doar
    /// detaliile plății („www.ghiseul.ro/mfinante”) dau o propunere, nu o clasificare.
    /// </summary>
    public static bool IsTaxAuthority(string? counterpartyName, string? counterpartyIban, AccountingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return IsTreasuryIban(counterpartyIban) ||
               !string.IsNullOrWhiteSpace(counterpartyName) &&
               Regex.IsMatch(Normalize(counterpartyName), options.TaxCounterpartyPattern, RegexOptions.CultureInvariant, PatternTimeout);
    }

    /// <summary>
    /// Cheia regulii învățate: numele contrapartidei sau, fără el, detaliile plății, normalizate, fără
    /// cifre (referințele lunare) și fără forme juridice.
    /// </summary>
    public static string NameKey(string? counterpartyName, string? remittance)
    {
        string source = string.IsNullOrWhiteSpace(counterpartyName) ? remittance ?? string.Empty : counterpartyName;
        string withoutDigits = new([.. NormalizeMerchant(source).Select(ch => char.IsDigit(ch) ? ' ' : ch)]);
        return string.Join(' ', withoutDigits.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Numele contrapartidei e al titularului: aceleași cuvinte (fără ordine, diacritice, forme juridice
    /// și marcaje de PFA), cel puțin două.
    /// </summary>
    public static bool IsOwner(string? counterpartyName, IEnumerable<string> ownerNames)
    {
        List<string> owners = [.. ownerNames];
        HashSet<string> counterparty = Words(counterpartyName);
        if (counterparty.Count >= 2 && owners.Select(Words).Any(owner => owner.Count >= 2 && owner.SetEquals(counterparty)))
        {
            return true;
        }

        // Numele prescurtat de bancă: „Victor I” = „Victor Ionescu” (un cuvânt întreg, restul inițiale).
        List<string> tokens = Tokens(counterpartyName);
        return tokens.Count >= 2 && tokens.Any(token => token.Length > 1) &&
               owners.Select(name => Tokens(name)).Any(owner => owner.Count == tokens.Count && SameWithInitials(owner, tokens));
    }

    /// <summary>Fiecare cuvânt are pereche: același cuvânt sau inițiala lui.</summary>
    private static bool SameWithInitials(List<string> owner, List<string> tokens)
    {
        var left = new List<string>(owner);
        foreach (string token in tokens.OrderByDescending(token => token.Length))
        {
            string? pair = left.FirstOrDefault(word => word == token) ??
                           (token.Length == 1 ? left.FirstOrDefault(word => word[0] == token[0]) : null);
            if (pair is null)
            {
                return false;
            }

            left.Remove(pair);
        }

        return true;
    }

    /// <summary>Cuvintele unui nume, inclusiv inițialele, fără cuvintele de transfer, marcaje și forme juridice.</summary>
    private static List<string> Tokens(string? name)
    {
        string normalized = $" {Normalize(name)} ";
        foreach (string marker in PfaMarkers)
        {
            normalized = normalized.Replace($" {marker} ", " ", StringComparison.Ordinal);
        }

        return [.. normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !LegalForms.Contains(word) && !TransferWords.Contains(word) && word.All(char.IsLetter))];
    }

    /// <summary>Majuscule, fără diacritice, spațiile comprimate (§5 pas 2).</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string decomposed = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char ch in decomposed)
        {
            // Punctul se scoate, nu devine spațiu: „S.R.L.” e „SRL”, o formă juridică, nu trei litere.
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && ch != '.')
            {
                builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }
        }

        return string.Join(' ', builder.ToString().Normalize(NormalizationForm.FormC).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Același comerciant, pe numele normalizate: unul îl conține pe celălalt sau încep cu același cuvânt
    /// semnificativ („OMV PETROM MARKETING” și „OMV PETROM SA”).
    /// </summary>
    public static bool SimilarMerchant(string? first, string? second)
    {
        string a = NormalizeMerchant(first);
        string b = NormalizeMerchant(second);
        if (a.Length < 3 || b.Length < 3)
        {
            return false;
        }

        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            return true;
        }

        string firstWord = a.Split(' ')[0];
        return firstWord.Length >= 3 && firstWord == b.Split(' ')[0];
    }

    /// <summary>Numele comerciantului pentru potrivire: normalizat, fără forme juridice.</summary>
    public static string NormalizeMerchant(string? value) =>
        string.Join(' ', Normalize(value).Split(' ').Where(word => !LegalForms.Contains(word)));

    /// <summary>IBAN-ul fără spații, cu majuscule.</summary>
    public static string? NormalizeIban(string? iban) =>
        string.IsNullOrWhiteSpace(iban) ? null : iban.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>Mascajul vechi al conturilor (primele 4 și ultimele 4 caractere), pentru datele salvate așa.</summary>
    public static string? MaskIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            return null;
        }

        string trimmed = iban.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return trimmed.Length <= 8 ? trimmed : $"{trimmed[..4]}••••{trimmed[^4..]}";
    }

    private static bool HasPfaMarker(string? name)
    {
        string normalized = $" {Normalize(name)} ";
        return PfaMarkers.Any(marker => normalized.Contains($" {marker} ", StringComparison.Ordinal));
    }

    private static HashSet<string> Words(string? name)
    {
        string normalized = $" {Normalize(name)} ";
        foreach (string marker in PfaMarkers)
        {
            normalized = normalized.Replace($" {marker} ", " ", StringComparison.Ordinal);
        }

        return [.. normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length > 1 && !LegalForms.Contains(word))];
    }
}
