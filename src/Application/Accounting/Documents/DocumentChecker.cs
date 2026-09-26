using System.Globalization;
using Application.Accounting.Contracts;
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

        if (invoice)
        {
            checks.Add(Arithmetic(fields));
            checks.Add(SupplierKnown(fields, context));
            checks.Add(VatIdFormat(fields));
            checks.Add(NotDuplicate(fields, context));
            checks.Add(NotAlreadyDeclared(context));
        }

        checks.Add(PeriodMatch(subject, fields, context.Options, invoice));
        checks.Add(CurrencyAllowed(fields, context.Options));
        checks.Add(SettlementCorrelation(fields, context, invoice));
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
        if (fields.Amount is { } amount)
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

    /// <summary>Regula de exigibilitate e DE CONFIRMAT (config): data facturii sau sfârșitul perioadei.</summary>
    private static DocumentCheck PeriodMatch(CheckSubject subject, ExtractedFields fields, AccountingOptions options, bool invoice)
    {
        DateOnly? reference = invoice && options.VatExigibility == VatExigibilityRule.InvoiceDate
            ? fields.InvoiceDate
            : fields.PeriodTo ?? fields.InvoiceDate;
        bool ok = reference is { } date && date.ToString("yyyy-MM", CultureInfo.InvariantCulture) == subject.Period;
        return ok
            ? new DocumentCheck(DocumentCheckCode.PeriodMatch, true, $"Data {AccountingJson.Date(reference)} e în perioada procesată.", null)
            : new DocumentCheck(
                DocumentCheckCode.PeriodMatch,
                false,
                $"Data {AccountingJson.Date(reference)} nu e în perioada procesată ({subject.Period}).",
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

    private static DocumentCheck SettlementCorrelation(ExtractedFields fields, CheckContext context, bool invoice)
    {
        decimal? income = invoice ? context.ReportIncome : fields.Amount;
        decimal minimum = context.Options.SettlementMinPercent;
        decimal maximum = context.Options.SettlementMaxPercent;

        if (income is not > 0 || fields.CommissionAmount is not { } commission)
        {
            return new DocumentCheck(
                DocumentCheckCode.SettlementCorrelation,
                true,
                "Raportul platformei nu e încă disponibil; corelarea se reface la pre-check.",
                null);
        }

        decimal ratio = Math.Round(commission / income.Value * 100, 2);
        bool ok = ratio >= minimum && ratio <= maximum;
        return new DocumentCheck(
            DocumentCheckCode.SettlementCorrelation,
            ok,
            $"Comision / venit = {AccountingJson.Amount(ratio)}% (interval acceptat {minimum.ToString(CultureInfo.InvariantCulture)}–{maximum.ToString(CultureInfo.InvariantCulture)}%).",
            null);
    }

    public static bool IsValidAt(IValidityPeriod rule, DateOnly date) =>
        rule.ValidFrom <= date && (rule.ValidTo is null || date <= rule.ValidTo);
}
