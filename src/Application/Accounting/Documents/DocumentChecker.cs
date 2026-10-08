using System.Globalization;
using Application.Accounting.Contracts;
using Application.Accounting.Tax;
using Domain.Accounting;

namespace Application.Accounting.Documents;

/// <summary>Documentul verificat: ce nu ține de câmpurile citite.</summary>
/// <param name="PdfText">Text layer-ul; <c>null</c> la un PDF scanat.</param>
public sealed record CheckSubject(string Period, PlatformDocumentType DocumentType, string? PdfText);

/// <summary>Ce trebuie aflat din baza de date înainte de verificări.</summary>
/// <param name="Suppliers">Registrul de furnizori (toate versiunile; valabilitatea se aplică aici).</param>
/// <param name="DuplicateInvoice">Există alt document cu același furnizor + număr de factură.</param>
/// <param name="DeclaredElsewhere">Declarația cu recipisă, pe altă perioadă, care include deja documentul.</param>
/// <param name="ReportIncome">La facturi: venitul din raportul aceleiași platforme și luni, dacă e citit.</param>
public sealed record CheckContext(
    IReadOnlyList<SupplierTaxProfile> Suppliers,
    bool DuplicateInvoice,
    string? DeclaredElsewhere,
    decimal? ReportIncome,
    AccountingOptions Options);

/// <summary>
/// Verificările deterministe ale unui document de platformă (spec contabilitate B1). Funcție pură:
/// nimic din AI, nimic din baza de date. Toate trec → <c>PENDING_CONFIRMATION</c>; oricare pică →
/// <c>NEEDS_REVIEW</c>.
/// </summary>
public static class DocumentChecker
{
    public static IReadOnlyList<DocumentCheck> Run(CheckSubject subject, ExtractedFields fields, CheckContext context)
    {
        bool invoice = subject.DocumentType == PlatformDocumentType.CommissionInvoice;
        var checks = new List<DocumentCheck> { AmountInText(subject, fields) };

        if (!invoice)
        {
            checks.Add(ReportArithmetic(fields));
        }

        if (invoice)
        {
            checks.Add(Arithmetic(fields));
            checks.Add(SupplierKnown(fields, context));
            checks.Add(VatIdFormat(fields));
            checks.Add(NotDuplicate(fields, context));
            checks.Add(NotAlreadyDeclared(context));
        }

        checks.Add(PeriodMatch(subject, fields, invoice));
        checks.Add(CurrencyAllowed(fields, context.Options));
        checks.Add(SettlementCorrelation(subject, fields, context, invoice));
        return checks;
    }

    public static bool AllPassed(IEnumerable<DocumentCheck> checks) => checks.All(check => check.Passed);

    /// <summary>
    /// Formele în care o sumă poate apărea în PDF: <c>1.000,00</c>, <c>1,000.00</c>, <c>1000.00</c>,
    /// <c>1000,00</c> (spec B1: normalizarea sumelor).
    /// </summary>
    public static IReadOnlyList<string> AmountSpellings(decimal value)
    {
        string plain = Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture);
        string grouped = Math.Abs(value).ToString("#,##0.00", CultureInfo.InvariantCulture);
        string sign = value < 0 ? "-" : string.Empty;
        return
        [
            sign + grouped.Replace(",", "#", StringComparison.Ordinal).Replace(".", ",", StringComparison.Ordinal).Replace("#", ".", StringComparison.Ordinal),
            sign + grouped,
            sign + plain,
            sign + plain.Replace(".", ",", StringComparison.Ordinal),
        ];
    }

    private static DocumentCheck AmountInText(CheckSubject subject, ExtractedFields fields)
    {
        var amounts = new List<(string Label, decimal Value)>();
        // Brutul compus din componentele raportului Bolt nu apare ca atare în PDF: se verifică
        // componentele lui, iar suma lor o verifică ReportArithmetic.
        if (fields.Amount is { } amount && amount != ReportComponents.Gross(fields))
        {
            amounts.Add(("total", amount));
        }

        if (fields.CommissionAmount is { } commission)
        {
            amounts.Add(("comision", commission));
        }

        amounts.AddRange(fields.OtherAmounts.Select(other => (other.Label, other.Amount)));

        if (subject.PdfText is null)
        {
            return new DocumentCheck(
                DocumentCheckCode.AmountInText,
                false,
                "PDF-ul nu are text: sumele nu pot fi verificate automat. Verifică-le în document și confirmă printr-o modificare.",
                null);
        }

        var missing = amounts
            .Where(item => !AmountSpellings(item.Value).Any(spelling => subject.PdfText.Contains(spelling, StringComparison.Ordinal)))
            .ToList();

        return missing.Count == 0
            ? new DocumentCheck(DocumentCheckCode.AmountInText, true, "Toate sumele citite apar în textul documentului.", null)
            : new DocumentCheck(
                DocumentCheckCode.AmountInText,
                false,
                string.Join(" ", missing.Select(item => $"Suma {AccountingJson.Amount(item.Value)} ({item.Label}) nu apare în textul documentului.")),
                null);
    }

    /// <summary>
    /// Raportul are venitul brut, iar la Bolt el e exact TOTAL-ul de tarif. Fără brut, venitul lunii
    /// nu ajunge în registre: decontul din bancă nu se poate descompune în venit și comision.
    /// </summary>
    private static DocumentCheck ReportArithmetic(ExtractedFields fields)
    {
        if (fields.Amount is not { } gross || gross <= 0)
        {
            return new DocumentCheck(
                DocumentCheckCode.Arithmetic,
                false,
                "Venitul brut (înainte de comision) lipsește din raport. Completează-l din document: fără el, venitul lunii nu intră în registre.",
                null);
        }

        if (ReportComponents.Gross(fields) is { } fares && fares != gross)
        {
            return new DocumentCheck(
                DocumentCheckCode.Arithmetic,
                false,
                $"Venitul brut e TOTAL-ul de tarif, {AccountingJson.Amount(fares)}, nu {AccountingJson.Amount(gross)}.",
                null);
        }

        if (fields.CashAmount is { } cash && cash > gross)
        {
            return new DocumentCheck(
                DocumentCheckCode.Arithmetic,
                false,
                $"Numerarul {AccountingJson.Amount(cash)} depășește venitul brut {AccountingJson.Amount(gross)}.",
                null);
        }

        return new DocumentCheck(DocumentCheckCode.Arithmetic, true, $"Venit brut {AccountingJson.Amount(gross)} lei, înainte de comision.", null);
    }

    private static DocumentCheck Arithmetic(ExtractedFields fields)
    {
        decimal vat = fields.OtherAmounts.FirstOrDefault(other => other.Label.Equals("TVA", StringComparison.OrdinalIgnoreCase))?.Amount ?? 0;
        decimal subtotal = fields.CommissionAmount ?? 0;
        decimal total = fields.Amount ?? 0;
        bool sumOk = Math.Abs(subtotal + vat - total) < 0.005m;

        if (!sumOk)
        {
            return new DocumentCheck(
                DocumentCheckCode.Arithmetic,
                false,
                $"Comision {AccountingJson.Amount(subtotal)} + TVA {AccountingJson.Amount(vat)} ≠ total {AccountingJson.Amount(total)}.",
                null);
        }

        return vat == 0
            ? new DocumentCheck(DocumentCheckCode.Arithmetic, true, "Comision + TVA = total; TVA 0 (taxare inversă).", null)
            : new DocumentCheck(
                DocumentCheckCode.Arithmetic,
                false,
                $"TVA-ul de pe factură trebuie să fie 0 (taxare inversă), nu {AccountingJson.Amount(vat)}.",
                null);
    }

    private static DocumentCheck SupplierKnown(ExtractedFields fields, CheckContext context)
    {
        SupplierTaxProfile? supplier = fields.SupplierVatId is { } vatId && fields.InvoiceDate is { } date
            ? context.Suppliers.FirstOrDefault(profile =>
                string.Equals(profile.VatId, vatId, StringComparison.OrdinalIgnoreCase) && IsValidAt(profile, date))
            : null;

        return supplier is not null
            ? new DocumentCheck(DocumentCheckCode.SupplierKnown, true, $"Furnizor identificat: {supplier.SupplierName}.", null)
            : new DocumentCheck(
                DocumentCheckCode.SupplierKnown,
                false,
                $"Furnizorul cu codul TVA {fields.SupplierVatId ?? "(lipsă)"} nu există în registrul de furnizori la data facturii.",
                DocumentCheckAction.AddSupplier);
    }

    private static DocumentCheck VatIdFormat(ExtractedFields fields)
    {
        string prefix = fields.SupplierVatId is { Length: >= 2 } vatId ? vatId[..2].ToUpperInvariant() : string.Empty;
        bool ok = !string.IsNullOrEmpty(fields.SupplierCountry) &&
                  string.Equals(prefix, fields.SupplierCountry, StringComparison.OrdinalIgnoreCase);
        string shownPrefix = prefix.Length == 0 ? "lipsă" : prefix;
        return ok
            ? new DocumentCheck(DocumentCheckCode.VatIdFormat, true, $"Prefixul codului TVA ({prefix}) corespunde țării furnizorului.", null)
            : new DocumentCheck(
                DocumentCheckCode.VatIdFormat,
                false,
                $"Prefixul codului TVA ({shownPrefix}) nu corespunde țării furnizorului ({fields.SupplierCountry ?? "lipsă"}).",
                null);
    }

    private static DocumentCheck NotDuplicate(ExtractedFields fields, CheckContext context) =>
        context.DuplicateInvoice
            ? new DocumentCheck(
                DocumentCheckCode.NotDuplicate,
                false,
                $"Există deja un document cu același furnizor și număr ({fields.InvoiceNumber}).",
                null)
            : new DocumentCheck(DocumentCheckCode.NotDuplicate, true, "Nu există alt document cu același furnizor și număr.", null);

    private static DocumentCheck NotAlreadyDeclared(CheckContext context) =>
        context.DeclaredElsewhere is { } declaration
            ? new DocumentCheck(DocumentCheckCode.NotAlreadyDeclared, false, $"Documentul apare deja în {declaration}, cu recipisă.", null)
            : new DocumentCheck(DocumentCheckCode.NotAlreadyDeclared, true, "Documentul nu a mai fost declarat.", null);

    /// <summary>
    /// Luna fiscală a documentului. La facturi, data impozitării, explicită (spec declarații F12): fără
    /// ea, documentul e de verificat și contabilul o completează; nu se deduce din perioada facturată.
    /// La rapoarte, sfârșitul perioadei raportate.
    /// </summary>
    private static DocumentCheck PeriodMatch(CheckSubject subject, ExtractedFields fields, bool invoice)
    {
        if (invoice && fields.TaxPointDate is null)
        {
            return new DocumentCheck(DocumentCheckCode.PeriodMatch, false, "Lipsește data impozitării; completeaz-o din factură.", null);
        }

        DateOnly? reference = invoice ? fields.TaxPointDate : fields.PeriodTo ?? fields.InvoiceDate;
        string label = invoice ? "Data impozitării" : "Data";
        bool ok = reference is { } date && date.ToString("yyyy-MM", CultureInfo.InvariantCulture) == subject.Period;
        return ok
            ? new DocumentCheck(DocumentCheckCode.PeriodMatch, true, $"{label} {AccountingJson.Date(reference)} e în perioada procesată.", null)
            : new DocumentCheck(
                DocumentCheckCode.PeriodMatch,
                false,
                $"{label} {AccountingJson.Date(reference)} nu e în perioada procesată ({subject.Period}).",
                null);
    }

    private static DocumentCheck CurrencyAllowed(ExtractedFields fields, AccountingOptions options)
    {
        bool ok = fields.Currency is { } currency &&
                  options.AllowedCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase);
        return ok
            ? new DocumentCheck(DocumentCheckCode.CurrencyAllowed, true, $"Moneda {fields.Currency} e acceptată.", null)
            : new DocumentCheck(
                DocumentCheckCode.CurrencyAllowed,
                false,
                $"Moneda {fields.Currency ?? "(lipsă)"} nu e acceptată (doar {string.Join(" / ", options.AllowedCurrencies)}).",
                null);
    }

    /// <summary>
    /// Comision / venit, ca avertisment: nu blochează documentul. O factură se compară cu raportul
    /// doar pe perioade echivalente (factura acoperă toată luna raportului); facturile săptămânale Uber
    /// se compară cu sumarul lunar abia la nivel de lună, în declarație.
    /// </summary>
    private static DocumentCheck SettlementCorrelation(CheckSubject subject, ExtractedFields fields, CheckContext context, bool invoice)
    {
        decimal minimum = context.Options.SettlementMinPercent;
        decimal maximum = context.Options.SettlementMaxPercent;

        if (invoice && !CoversMonth(subject.Period, fields))
        {
            return new DocumentCheck(
                DocumentCheckCode.SettlementCorrelation,
                true,
                $"Factura acoperă {AccountingJson.Date(fields.PeriodFrom)}–{AccountingJson.Date(fields.PeriodTo)}, raportul toată luna: corelarea se face pe lună, în declarație.",
                null);
        }

        decimal? income = invoice ? context.ReportIncome : fields.Amount;
        if (income is not > 0 || fields.CommissionAmount is not { } commission)
        {
            return new DocumentCheck(
                DocumentCheckCode.SettlementCorrelation,
                true,
                "Raportul platformei nu e încă disponibil; corelarea se reface la pre-check.",
                null);
        }

        decimal ratio = Math.Round(commission / income.Value * 100, 2);
        bool inRange = ratio >= minimum && ratio <= maximum;
        return new DocumentCheck(
            DocumentCheckCode.SettlementCorrelation,
            true,
            $"Comision / venit = {AccountingJson.Amount(ratio)}% (interval obișnuit {minimum.ToString(CultureInfo.InvariantCulture)}–{maximum.ToString(CultureInfo.InvariantCulture)}%)." +
            (inRange ? string.Empty : " De verificat, nu blochează."),
            null,
            Warning: !inRange);
    }

    /// <summary>Perioada facturii e exact luna calendaristică procesată.</summary>
    private static bool CoversMonth(string period, ExtractedFields fields)
    {
        if (!DateOnly.TryParseExact($"{period}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly start))
        {
            return false;
        }

        return fields.PeriodFrom == start && fields.PeriodTo == start.AddMonths(1).AddDays(-1);
    }

    public static bool IsValidAt(IValidityPeriod rule, DateOnly date) =>
        rule.ValidFrom <= date && (rule.ValidTo is null || date <= rule.ValidTo);
}
