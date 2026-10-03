using System.Globalization;
using Application.Accounting.Contracts;
using Domain.Accounting;

namespace Application.Accounting.Tax;

/// <summary>O factură de comision confirmată, intrare în calcul.</summary>
/// <param name="Label">Cum apare în explicații („Factura Bolt EE-BOLT-2026-08-1000”).</param>
/// <param name="ServicePeriodEnd">Sfârșitul perioadei facturate, pentru regula de exigibilitate.</param>
/// <param name="TaxPointDate">„Data impozitării” de pe factură, dacă apare.</param>
public sealed record TaxInvoice(
    Guid DocumentId,
    string Label,
    string? SupplierVatId,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    DateOnly? ServicePeriodEnd,
    string? Currency,
    decimal? CommissionAmount,
    DateOnly? TaxPointDate = null,
    Platform? Platform = null);

/// <summary>
/// Un raport de platformă confirmat: veniturile din curse, care nu intră în bază. Comisionul și
/// reținerea la sursă din raport servesc doar la avertismente (corelare, D100), nu la calcul.
/// </summary>
public sealed record TaxReport(
    Guid DocumentId,
    string? Currency,
    decimal? Income,
    DateOnly? PeriodTo,
    Platform? Platform = null,
    decimal? Commission = null,
    decimal? WithheldTax = null);

/// <summary>Codurile și termenul unei declarații, din regulile perioadei (spec declarații F24).</summary>
public sealed record DeclarationRules(string? ObligationCode, string? BudgetCode, DateOnly? DueDate);

/// <summary>
/// Regulile unei perioade, rezolvate din <see cref="TaxRuleSet"/> (spec declarații §4) și înghețate
/// în snapshot-ul declarației: statele UE, cursul, rotunjirea, codurile și termenele.
/// </summary>
/// <param name="RulesetVersion">Ce reguli erau active, ca versiune a setului.</param>
public sealed record TaxEngineSettings(
    IReadOnlyCollection<string> EuCountries,
    ExchangeRateDateRule ExchangeRateDate,
    IReadOnlyDictionary<DeclarationType, DeclarationRounding> Rounding,
    IReadOnlyDictionary<DeclarationType, DeclarationRules>? Declarations = null,
    string? RulesetVersion = null)
{
    /// <summary>Regulile valabile la sfârșitul perioadei <c>yyyy-MM</c>.</summary>
    public static TaxEngineSettings ForPeriod(TaxRuleSet rules, string period)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var start = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateOnly end = start.AddMonths(1).AddDays(-1);
        static string Code(DeclarationType type) => type.ToString();

        return new TaxEngineSettings(
            [.. rules.Rules.Where(rule => rule.RuleType == TaxRuleTypes.EuMember && rules.IsEuMember(rule.Jurisdiction, end)).Select(rule => rule.Jurisdiction).Distinct()],
            Enum.TryParse(rules.Find(TaxRuleTypes.ExchangeRate, "RO", end)?.Formula, out ExchangeRateDateRule exchange) ? exchange : ExchangeRateDateRule.SameDayOrPrevious,
            Enum.GetValues<DeclarationType>()
                .Select(type => (type, rule: rules.Find(TaxRuleTypes.Rounding, "RO", end, new TaxRuleContext(Code(type)))))
                .Where(pair => pair.rule is not null && Enum.TryParse(pair.rule.Formula, out DeclarationRounding _))
                .ToDictionary(pair => pair.type, pair => Enum.Parse<DeclarationRounding>(pair.rule!.Formula!)),
            Enum.GetValues<DeclarationType>().ToDictionary(type => type, type => new DeclarationRules(
                (rules.Find(TaxRuleTypes.ObligationCode, "RO", end, new TaxRuleContext(Code(type), IncomeType: "COMMISSION")) ??
                 rules.Find(TaxRuleTypes.ObligationCode, "RO", end, new TaxRuleContext(Code(type))))?.Formula,
                (rules.Find(TaxRuleTypes.BudgetCode, "RO", end, new TaxRuleContext(Code(type), IncomeType: "COMMISSION")) ??
                 rules.Find(TaxRuleTypes.BudgetCode, "RO", end, new TaxRuleContext(Code(type))))?.Formula,
                rules.Find(TaxRuleTypes.Deadline, "RO", end, new TaxRuleContext(Code(type))) is { Formula: { } formula }
                    ? DeclarationDeadline.Of(formula, period)
                    : null)),
            rules.VersionAt(end));
    }
}

/// <summary>
/// Intrarea calculului pentru un PFA și o lună (spec contabilitate B2): documentele confirmate,
/// regulile cu toate versiunile lor (valabilitatea se aplică la data fiecărei facturi), cursurile
/// și setarea art. 317 cu istoricul ei.
/// </summary>
public sealed record PfaTaxInput(
    string Period,
    IReadOnlyList<TaxInvoice> Invoices,
    IReadOnlyList<TaxReport> Reports,
    IReadOnlyList<SupplierTaxProfile> Suppliers,
    IReadOnlyList<VatRate> VatRates,
    IReadOnlyList<D100Rule> D100Rules,
    IReadOnlyList<ExchangeRate> ExchangeRates,
    IReadOnlyList<Art317Period> Art317,
    TaxEngineSettings Settings);

/// <summary>Un interval în care codul special de TVA art. 317 e (sau nu e) activ.</summary>
public sealed record Art317Period(bool Enabled, DateOnly ValidFrom);

/// <summary>O linie de calcul: <c>bază × cotă = valoare</c>, cu documentul sursă.</summary>
/// <param name="AmountInCurrency">Suma în moneda facturii (D301, secțiunea 4.1).</param>
public sealed record TaxLine(
    Guid SourceDocumentId,
    string SourceDocumentLabel,
    string RuleCode,
    decimal AmountInCurrency,
    decimal Base,
    decimal? Rate,
    decimal Value,
    string Currency,
    decimal? ExchangeRate,
    string Explanation,
    string SupplierName,
    string SupplierCountry,
    string SupplierVatId,
    string? OperationType,
    string? Treaty,
    DateOnly? ResidenceCertValidFrom,
    DateOnly? ResidenceCertValidTo,
    IReadOnlyList<Guid> DocumentIds);

/// <param name="Warnings">Nu blochează: diferențe de semnalat contabilului (corelare, reținere la sursă).</param>
/// <param name="Withholding">D100: reținerea raportată de platformă lângă impozitul calculat.</param>
public sealed record DeclarationCalculation(
    DeclarationType Type,
    bool Applicable,
    IReadOnlyList<TaxLine> Lines,
    decimal Total,
    string Explanation,
    decimal? ExcludedRideIncome,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<WithholdingComparison>? Withholding = null);

public sealed record TaxResult(IReadOnlyList<string> BlockingReasons, IReadOnlyDictionary<DeclarationType, DeclarationCalculation> Declarations)
{
    public bool IsBlocked => BlockingReasons.Count > 0;
}

/// <summary>
/// <c>D100_RENT_INDIVIDUAL</c>: interfața există, calculul nu. Regula e DE CONFIRMAT (bază, cotă,
/// sursa datelor — §6 pct. 7); până atunci nu produce nicio linie, chiar dacă ar fi activată.
/// </summary>
public static class D100RentIndividualRule
{
    public static IReadOnlyList<TaxLine> Calculate(PfaTaxInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return [];
    }
}

/// <summary>
/// Motorul fiscal lunar (spec contabilitate B2): facturi de comision confirmate → D100, D301, D390.
/// Funcție pură, fără bază de date: ce intră determină complet ce iese, deci se testează pe
/// exemple „golden”. Nicio cotă nu e scrisă aici; toate vin din reguli, cu valabilitatea lor.
/// </summary>
public static class MonthlyTaxEngine
{
    public const string D100CommissionRule = "D100_COMMISSION_NONRESIDENT";
    public const string D301Rule = "D301_EU_SERVICES";
    public const string D390Rule = "D390_EU_SERVICES";
    public const string D390ServicesOperation = "S";

    public static TaxResult Calculate(PfaTaxInput input)
    {
        var blocking = new List<string>();
        var d100 = new List<TaxLine>();
        var d301 = new List<TaxLine>();

        foreach (TaxInvoice invoice in input.Invoices)
        {
            CalculateInvoice(input, invoice, blocking, d100, d301);
        }

        if (input.D100Rules.Any(rule => rule.Code == D100RuleCode.D100RentIndividual && rule.Enabled && !rule.PendingConfirmation))
        {
            d100.AddRange(D100RentIndividualRule.Calculate(input));
        }

        List<TaxLine> d390 = [.. d301
            .GroupBy(line => line.SupplierVatId)
            .Select(group =>
            {
                TaxLine first = group.First();
                decimal @base = group.Sum(line => line.Base);
                return first with
                {
                    SourceDocumentLabel = string.Join(", ", group.Select(line => line.SourceDocumentLabel)),
                    RuleCode = D390Rule,
                    AmountInCurrency = @base,
                    Base = @base,
                    Rate = null,
                    Value = 0,
                    Currency = "RON",
                    ExchangeRate = null,
                    Explanation = $"{D390ServicesOperation} / {first.SupplierCountry} / {first.SupplierName} / {AccountingJson.Amount(@base)}",
                    OperationType = D390ServicesOperation,
                    DocumentIds = [.. group.SelectMany(line => line.DocumentIds)],
                };
            })];

        decimal rideIncome = 0;
        foreach (TaxReport report in input.Reports)
        {
            if (report.Income is { } income && ToRon(input, income, report.Currency, report.PeriodTo, blocking, "Raportul platformei") is { } ron)
            {
                rideIncome += ron.Amount;
            }
        }

        // F17: D390 și D301 vin din aceleași facturi; bazele lunii trebuie să fie egale.
        decimal d301Base = d301.Sum(line => line.Base);
        decimal d390Base = d390.Sum(line => line.Base);
        if (d301Base != d390Base)
        {
            blocking.Add($"Baza D390 ({AccountingJson.Amount(d390Base)} lei) diferă de baza D301 ({AccountingJson.Amount(d301Base)} lei).");
        }

        decimal d100Total = Total(input, DeclarationType.D100, d100);
        decimal d301Total = Total(input, DeclarationType.D301, d301);
        (List<string> d301Warnings, List<string> d100Warnings, List<WithholdingComparison> withholding) = ReportWarnings(input, d100, d301);

        return new TaxResult(
            blocking.Distinct().ToList(),
            new Dictionary<DeclarationType, DeclarationCalculation>
            {
                [DeclarationType.D100] = new(
                    DeclarationType.D100, d100.Count > 0, d100, d100Total,
                    $"Impozit pe veniturile nerezidenților din comisioane: {AccountingJson.Amount(d100Total)} lei.", null, d100Warnings, withholding),
                [DeclarationType.D301] = new(
                    DeclarationType.D301, d301.Count > 0, d301, d301Total,
                    $"TVA pentru serviciile intracomunitare achiziționate: {AccountingJson.Amount(d301Total)} lei. Veniturile din curse nu intră în bază.",
                    rideIncome,
                    d301Warnings),
                [DeclarationType.D390] = new(DeclarationType.D390, d390.Count > 0, d390, 0, "0 lei de plată, doar raportare.", null),
            });
    }

    private static void CalculateInvoice(PfaTaxInput input, TaxInvoice invoice, List<string> blocking, List<TaxLine> d100, List<TaxLine> d301)
    {
        if (invoice.InvoiceDate is not { } date || invoice.CommissionAmount is not { } commission)
        {
            blocking.Add($"{invoice.Label}: lipsește data sau comisionul.");
            return;
        }

        SupplierTaxProfile? supplier = input.Suppliers.FirstOrDefault(profile =>
            string.Equals(profile.VatId, invoice.SupplierVatId, StringComparison.OrdinalIgnoreCase) && IsValidAt(profile, date));
        if (supplier is null)
        {
            blocking.Add($"{invoice.Label}: furnizor necunoscut ({invoice.SupplierVatId ?? "fără cod TVA"}) la {AccountingJson.Date(date)}.");
            return;
        }

        if (ToRon(input, commission, invoice.Currency, date, blocking, invoice.Label) is not { } converted)
        {
            return;
        }

        string conversion = converted.Rate is { } rate
            ? $" ({AccountingJson.Amount(commission)} {invoice.Currency} × {rate.ToString("0.####", CultureInfo.InvariantCulture)})"
            : string.Empty;
        var common = new TaxLine(
            invoice.DocumentId, invoice.Label, string.Empty, commission, converted.Amount, null, 0, invoice.Currency ?? "RON", converted.Rate,
            string.Empty, supplier.SupplierName, supplier.Country, supplier.VatId, null, null, null, null, [invoice.DocumentId]);

        CalculateD100(input, supplier, date, common, conversion, blocking, d100);

        // F11: declarabil după furnizorul juridic și țara lui (registrul de furnizori), niciodată după brand.
        if (!input.Settings.EuCountries.Contains(supplier.Country, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        // F12: luna și cota vin din data impozitării, explicită pe factură. Fără ea nu se ghicește
        // (nici din perioada facturată, nici din numele raportului): Stop.
        if (invoice.TaxPointDate is not { } taxPoint)
        {
            blocking.Add($"{invoice.Label}: lipsește data impozitării; fără ea nu se generează D301 și D390.");
            return;
        }

        // F04: art. 317 activ la data impozitării, altfel Stop și onboarding fiscal (task D700).
        if (!Art317Active(input, taxPoint))
        {
            blocking.Add($"{Art317Missing} la {AccountingJson.Date(taxPoint)}.");
            return;
        }

        VatRate? vatRate = input.VatRates.FirstOrDefault(rate => IsValidAt(rate, taxPoint));
        if (vatRate is null)
        {
            blocking.Add($"Nu există cotă de TVA valabilă la {AccountingJson.Date(taxPoint)}.");
            return;
        }

        // F13: baza în lei la cursul regulii perioadei, la data impozitării; cursul rămâne pe linie.
        if (ToRon(input, commission, invoice.Currency, taxPoint, blocking, invoice.Label) is not { } atTaxPoint)
        {
            return;
        }

        string taxPointConversion = atTaxPoint.Rate is { } taxPointRate
            ? $" ({AccountingJson.Amount(commission)} {invoice.Currency} × {taxPointRate.ToString("0.####", CultureInfo.InvariantCulture)})"
            : string.Empty;
        decimal vat = Line(atTaxPoint.Amount * vatRate.Rate / 100);
        d301.Add(common with
        {
            RuleCode = D301Rule,
            Base = atTaxPoint.Amount,
            ExchangeRate = atTaxPoint.Rate,
            Rate = vatRate.Rate,
            Value = vat,
            Explanation = Calculation(atTaxPoint.Amount, vatRate.Rate, vat) + taxPointConversion,
        });
    }

    /// <summary>Începutul mesajului de blocare pentru art. 317 inactiv (F04): după el se creează task-ul D700.</summary>
    public const string Art317Missing = "Codul special de TVA art. 317 nu e activ";

    private static void CalculateD100(
        PfaTaxInput input,
        SupplierTaxProfile supplier,
        DateOnly date,
        TaxLine common,
        string conversion,
        List<string> blocking,
        List<TaxLine> d100)
    {
        D100Rule? rule = input.D100Rules.FirstOrDefault(r => r.Code == D100RuleCode.D100CommissionNonresident && IsValidAt(r, date));
        if (rule is not { Enabled: true })
        {
            return;
        }

        bool certificateValid = supplier.ResidenceCertValidFrom is { } from && supplier.ResidenceCertValidTo is { } to && from <= date && date <= to;
        if (!certificateValid)
        {
            blocking.Add($"Certificatul de rezidență pentru {supplier.SupplierName} nu e valabil la {AccountingJson.Date(date)}.");
            return;
        }

        if (!supplier.D100RateConfirmed || supplier.D100Rate is not { } rate)
        {
            blocking.Add($"Cota D100 pentru {supplier.SupplierName} nu e confirmată.");
            return;
        }

        decimal value = Line(common.Base * rate / 100);
        d100.Add(common with
        {
            RuleCode = D100CommissionRule,
            Rate = rate,
            Value = value,
            Explanation = Calculation(common.Base, rate, value) + conversion,
            Treaty = supplier.Treaty,
            ResidenceCertValidFrom = supplier.ResidenceCertValidFrom,
            ResidenceCertValidTo = supplier.ResidenceCertValidTo,
        });
    }

    /// <summary>
    /// Avertismentele din rapoartele platformelor, pe platformă (spec: documente reale Uber/Bolt):
    /// <list type="bullet">
    /// <item>D301: suma facturilor din lună față de comisionul din raport. Sumarul Uber e pe lună
    /// calendaristică, facturile săptămânale pe perioadele lor, deci diferența e normală și nu blochează.</item>
    /// <item>D100: reținerea la sursă raportată („Reținere la sursă” la Bolt) față de impozitul
    /// calculat. Se afișează, nu se corectează.</item>
    /// </list>
    /// </summary>
    private static (List<string> D301, List<string> D100, List<WithholdingComparison> Withholding) ReportWarnings(
        PfaTaxInput input, List<TaxLine> d100, List<TaxLine> d301)
    {
        var d301Warnings = new List<string>();
        var d100Warnings = new List<string>();
        var withholding = new List<WithholdingComparison>();
        var platformOf = input.Invoices.ToDictionary(invoice => invoice.DocumentId, invoice => invoice.Platform);

        foreach (TaxReport report in input.Reports.Where(report => report.Platform is not null))
        {
            string name = PlatformLabels.Name(report.Platform);
            bool Of(TaxLine line) => platformOf.GetValueOrDefault(line.SourceDocumentId) == report.Platform;

            if (report.Commission is { } commission && IsRon(report.Currency) && d301.Any(Of))
            {
                decimal invoiced = d301.Where(Of).Sum(line => line.Base);
                if (Math.Abs(invoiced - commission) >= 0.01m)
                {
                    d301Warnings.Add(
                        $"{name}: facturile de comision din lună însumează {AccountingJson.Amount(invoiced)} lei, raportul arată comision " +
                        $"{AccountingJson.Amount(commission)} lei. Raportul poate acoperi altă perioadă decât facturile; diferența nu blochează.");
                }
            }

            if (report.WithheldTax is { } reported && IsRon(report.Currency))
            {
                decimal calculated = d100.Where(Of).Sum(line => line.Value);
                withholding.Add(new WithholdingComparison(name, reported, calculated));
                if (Math.Abs(calculated - reported) >= 0.01m)
                {
                    d100Warnings.Add(
                        $"{name}: reținerea la sursă raportată e {AccountingJson.Amount(reported)} lei, D100 calculat {AccountingJson.Amount(calculated)} lei " +
                        $"(diferență {AccountingJson.Amount(calculated - reported)} lei). Nu se corectează automat.");
                }
            }
        }

        return (d301Warnings, d100Warnings, withholding);
    }

    private static bool IsRon(string? currency) => currency is null || currency.Equals("RON", StringComparison.OrdinalIgnoreCase);

    /// <summary>Suma în lei și cursul folosit. Sursa și ziua cursului sunt DE CONFIRMAT (config).</summary>
    private static (decimal Amount, decimal? Rate)? ToRon(PfaTaxInput input, decimal amount, string? currency, DateOnly? date, List<string> blocking, string label)
    {
        if (currency is null || currency.Equals("RON", StringComparison.OrdinalIgnoreCase))
        {
            return (amount, null);
        }

        ExchangeRate? rate = date is { } day ? PickRate(input.ExchangeRates, currency, day, input.Settings.ExchangeRateDate) : null;
        if (rate is null)
        {
            blocking.Add($"{label}: lipsește cursul {currency} la {AccountingJson.Date(date)}.");
            return null;
        }

        return (Line(amount * rate.Rate), rate.Rate);
    }

    /// <summary>Cursul aplicabil unei date, după regula din configurare.</summary>
    public static ExchangeRate? PickRate(IEnumerable<ExchangeRate> rates, string currency, DateOnly date, ExchangeRateDateRule rule) =>
        rates
            .Where(rate => rate.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase) &&
                           (rule == ExchangeRateDateRule.PreviousPublication ? rate.Date < date : rate.Date <= date))
            .OrderByDescending(rate => rate.Date)
            .FirstOrDefault();

    private static bool Art317Active(PfaTaxInput input, DateOnly date) =>
        input.Art317.Where(period => period.ValidFrom <= date).OrderByDescending(period => period.ValidFrom).FirstOrDefault()?.Enabled == true;

    /// <summary>Totalul declarației, rotunjit după configurare (DE CONFIRMAT); liniile rămân cu zecimale.</summary>
    private static decimal Total(PfaTaxInput input, DeclarationType type, IEnumerable<TaxLine> lines)
    {
        decimal total = lines.Sum(line => line.Value);
        return input.Settings.Rounding.TryGetValue(type, out DeclarationRounding rounding) && rounding == DeclarationRounding.WholeLei
            ? Math.Round(total, 0, MidpointRounding.AwayFromZero)
            : total;
    }

    /// <summary>Calculul pe linii păstrează doi zecimali (bani), fără rotunjirea declarației.</summary>
    private static decimal Line(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Calculation(decimal @base, decimal rate, decimal value) =>
        $"{AccountingJson.Amount(@base)} × {rate.ToString("0.##", CultureInfo.InvariantCulture).Replace(".", ",", StringComparison.Ordinal)}% = {AccountingJson.Amount(value)}";

    private static bool IsValidAt(IValidityPeriod rule, DateOnly date) =>
        rule.ValidFrom <= date && (rule.ValidTo is null || date <= rule.ValidTo);
}
