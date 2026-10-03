using System.Text.Json;
using Application.Accounting;
using Application.Accounting.Tax;
using Domain.Accounting;
using Infrastructure.Accounting;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// B2: motorul fiscal lunar. Cazurile golden din <c>Golden/*.json</c> rulează toate automat; cele
/// cerute explicit de spec (EUR, certificat expirat, TVA schimbat în lună, lună fără UE) sunt aici.
/// </summary>
public sealed class MonthlyTaxEngineTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> GoldenCases()
    {
        var data = new TheoryData<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Accounting", "Golden"), "*.json"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void Golden_case(string file)
    {
        GoldenCase golden = JsonSerializer.Deserialize<GoldenCase>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Accounting", "Golden", file)), Json)!;

        // F20: comisionul fiecărei facturi e plătit (reținut la decontare) la data facturii.
        PfaTaxInput input = golden.ToInput();
        TaxResult result = MonthlyTaxEngine.Calculate(input with { NonResident = Payments(input.Invoices, input.Suppliers) });

        result.BlockingReasons.ShouldBe(golden.Expected.BlockingReasons, file);
        foreach ((string type, ExpectedDeclaration expected) in golden.Expected.Declarations())
        {
            DeclarationCalculation actual = result.Declarations[Enum.Parse<DeclarationType>(type)];
            actual.Total.ShouldBe(expected.Total, $"{file} {type}");
            if (expected.ExcludedRideIncome is { } income)
            {
                actual.ExcludedRideIncome.ShouldBe(income, $"{file} {type}");
            }

            actual.Lines.Count.ShouldBe(expected.Lines.Count, $"{file} {type}");
            foreach ((TaxLine line, ExpectedLine want) in actual.Lines.Zip(expected.Lines))
            {
                line.SupplierVatId.ShouldBe(want.SupplierVatId);
                line.Base.ShouldBe(want.Base, $"{file} {type} {want.SupplierVatId}");
                line.Value.ShouldBe(want.Value, $"{file} {type} {want.SupplierVatId}");
                if (want.Rate is { } rate)
                {
                    line.Rate.ShouldBe(rate);
                }

                if (want.OperationType is { } operation)
                {
                    line.OperationType.ShouldBe(operation);
                    line.SupplierCountry.ShouldBe(want.Country);
                }
            }
        }
    }

    [Fact]
    public void Ion_popescu_lines_explain_the_calculation()
    {
        TaxResult result = MonthlyTaxEngine.Calculate(Input());

        result.Declarations[DeclarationType.D100].Lines[0].Explanation.ShouldBe("1.000,00 × 2% = 20,00 (plata din 31.08.2026)");
        result.Declarations[DeclarationType.D301].Lines.Select(line => line.Explanation).ShouldBe(["1.000,00 × 21% = 210,00", "600,00 × 21% = 126,00"]);
        result.Declarations[DeclarationType.D390].Lines[0].Explanation.ShouldBe("S / EE / Bolt Operations OÜ / 1.000,00");
    }

    [Fact]
    public void Eur_invoice_is_converted_at_the_rate_of_the_invoice_date()
    {
        TaxInvoice eur = Uber(150m) with { Currency = "EUR" };
        PfaTaxInput input = Input(invoices: [Bolt(1000m), eur]) with
        {
            ExchangeRates =
            [
                new ExchangeRate { Currency = "EUR", Date = new DateOnly(2026, 8, 28), Rate = 5.07m, Source = "BNR" },
                new ExchangeRate { Currency = "EUR", Date = new DateOnly(2026, 8, 31), Rate = 5.08m, Source = "BNR" },
                new ExchangeRate { Currency = "EUR", Date = new DateOnly(2026, 9, 1), Rate = 5.10m, Source = "BNR" },
            ],
        };

        TaxLine line = MonthlyTaxEngine.Calculate(input).Declarations[DeclarationType.D301].Lines[1];

        line.AmountInCurrency.ShouldBe(150m);
        line.ExchangeRate.ShouldBe(5.08m);
        line.Base.ShouldBe(762m);
        line.Value.ShouldBe(160.02m);
        line.Explanation.ShouldBe("762,00 × 21% = 160,02 (150,00 EUR × 5.08)");
    }

    [Fact]
    public void Previous_publication_rule_uses_the_rate_before_the_invoice_date()
    {
        ExchangeRate[] rates =
        [
            new() { Currency = "EUR", Date = new DateOnly(2026, 8, 28), Rate = 5.07m, Source = "BNR" },
            new() { Currency = "EUR", Date = new DateOnly(2026, 8, 31), Rate = 5.08m, Source = "BNR" },
        ];

        MonthlyTaxEngine.PickRate(rates, "EUR", new DateOnly(2026, 8, 31), ExchangeRateDateRule.PreviousPublication)!.Rate.ShouldBe(5.07m);
        MonthlyTaxEngine.PickRate(rates, "EUR", new DateOnly(2026, 8, 30), ExchangeRateDateRule.SameDayOrPrevious)!.Rate.ShouldBe(5.07m);
    }

    [Fact]
    public void Missing_exchange_rate_blocks() =>
        MonthlyTaxEngine.Calculate(Input(invoices: [Uber(150m) with { Currency = "EUR" }])).BlockingReasons
            .ShouldContain(reason => reason.Contains("lipsește cursul EUR", StringComparison.Ordinal));

    /// <summary>Scenariul 7, F22–F23: certificat expirat la data plății → fallback, decizie de confirmat, doar D100 blocată.</summary>
    [Fact]
    public void S7_F22_F23_ExpiredResidenceCertificateBlocksOnlyD100()
    {
        SupplierTaxProfile expired = BoltProfile();
        expired.ResidenceCertValidTo = new DateOnly(2026, 7, 31);

        TaxResult result = MonthlyTaxEngine.Calculate(Input(suppliers: [expired, UberProfile()]));

        result.IsBlocked.ShouldBeFalse();
        result.Declarations[DeclarationType.D301].IsBlocked.ShouldBeFalse();
        result.Declarations[DeclarationType.D100].Blockers!.ShouldContain(reason =>
            reason.Contains("Certificatul de rezidență pentru Bolt Operations OÜ nu e valabil la 31.08.2026; cota de fallback 16%.", StringComparison.Ordinal));
    }

    /// <summary>F23: o regulă de tratat neconfirmată cere confirmarea Adminului; D100 așteaptă.</summary>
    [Fact]
    public void F23_UnconfirmedTreatyRuleNeedsLegalConfirmation()
    {
        SupplierTaxProfile uber = UberProfile();
        uber.D100RateConfirmed = false;

        TaxResult result = MonthlyTaxEngine.Calculate(Input(suppliers: [BoltProfile(), uber]));

        result.IsBlocked.ShouldBeFalse();
        result.Declarations[DeclarationType.D100].Blockers!.ShouldContain(reason => reason.Contains("Regula de tratat pentru Uber B.V. (0%) nu e confirmată.", StringComparison.Ordinal));
    }

    /// <summary>Scenariul 6, F20: factura din septembrie plătită în octombrie → D100 în octombrie, nu în septembrie.</summary>
    [Fact]
    public void S6_F20_TheD100MonthIsThePaymentMonth()
    {
        TaxInvoice september = Bolt(1000m) with { InvoiceDate = new DateOnly(2026, 9, 30), TaxPointDate = new DateOnly(2026, 9, 30) };
        List<NonResidentLine> paidInOctober = Payments([september], [BoltProfile()], new DateOnly(2026, 10, 5));
        PfaTaxInput sept = Input(invoices: [september]) with { Period = "2026-09", NonResident = [] };
        PfaTaxInput oct = Input(invoices: []) with { Period = "2026-10", NonResident = paidInOctober };

        MonthlyTaxEngine.Calculate(sept).Declarations[DeclarationType.D100].Applicable.ShouldBeFalse();
        MonthlyTaxEngine.Calculate(oct).Declarations[DeclarationType.D100].Total.ShouldBe(20m);
    }

    /// <summary>F21, F22, F24: regula după codul fiscal al entității; fără regulă de tratat, fallback-ul; codul 634 din configurare.</summary>
    [Fact]
    public void F21_F22_F24_TheRuleFollowsTheLegalEntityAndFallsBack()
    {
        var payment = new NonResidentPayment
        {
            Id = Guid.NewGuid(), SupplierLegalName = "Bolt Operations OÜ", SupplierCountry = "EE", SupplierTaxId = "EE102090374",
            PaymentDate = new DateOnly(2026, 8, 31), GrossIncomeRon = 1000m, IncomeType = "COMMISSION",
        };

        NonResidentDecisionResult treaty = NonResidentTaxEngine.Decide(payment, [BoltProfile()], new TaxRuleSet(TaxRuleSeed.Rules));
        (treaty.Rate, treaty.TaxDue, treaty.ObligationCode, treaty.Status).ShouldBe((2m, 20m, "634", NonResidentDecisionStatus.Auto));

        SupplierTaxProfile withoutRule = BoltProfile();
        withoutRule.D100Rate = null;
        List<TaxRule> confirmedFallback = [.. TaxRuleSeed.Rules];
        confirmedFallback.Single(rule => rule.RuleType == TaxRuleTypes.NonResidentRate).Confirmed = true;
        NonResidentDecisionResult fallback = NonResidentTaxEngine.Decide(payment, [withoutRule], new TaxRuleSet(confirmedFallback));
        (fallback.Rate, fallback.TaxDue, fallback.Status).ShouldBe((16m, 160m, NonResidentDecisionStatus.Auto));
    }

    /// <summary>F14: cota de TVA e cea valabilă la data impozitării, nu la data facturii.</summary>
    [Fact]
    public void F14_VatRateChangedMidMonthUsesTheTaxPointDate()
    {
        VatRate[] rates =
        [
            new() { Rate = 19, ValidFrom = new DateOnly(2017, 1, 1), ValidTo = new DateOnly(2026, 8, 15) },
            new() { Rate = 21, ValidFrom = new DateOnly(2026, 8, 16) },
        ];
        TaxInvoice invoice = Bolt(1000m) with { TaxPointDate = new DateOnly(2026, 8, 9) };

        MonthlyTaxEngine.Calculate(Input(invoices: [invoice]) with { VatRates = rates }).Declarations[DeclarationType.D301].Total.ShouldBe(190m);
    }

    /// <summary>F12 și scenariul 3: o factură UE fără dată de impozitare oprește D301 și D390; luna nu se ghicește.</summary>
    [Fact]
    public void F12_S3_AnEuInvoiceWithoutTaxPointDateStops()
    {
        TaxResult result = MonthlyTaxEngine.Calculate(Input(invoices: [Bolt(1000m) with { TaxPointDate = null }]));

        result.IsBlocked.ShouldBeTrue();
        result.BlockingReasons.ShouldContain(reason => reason.Contains("lipsește data impozitării", StringComparison.Ordinal));
    }

    /// <summary>Scenariul 1 și F15–F17: o factură UE de 1.000 lei, cota 21% → D301 1.000 / 210, D390 un rând S, bazele egale.</summary>
    [Fact]
    public void S1_F15_F16_F17_OneEuInvoiceGivesMatchingD301AndD390()
    {
        TaxResult result = MonthlyTaxEngine.Calculate(Input(invoices: [Bolt(1000m) with { TaxPointDate = new DateOnly(2026, 9, 15) }]) with { Period = "2026-09" });

        result.IsBlocked.ShouldBeFalse();
        DeclarationCalculation d301 = result.Declarations[DeclarationType.D301];
        (d301.Lines.Sum(line => line.Base), d301.Total).ShouldBe((1000m, 210m));
        TaxLine row = result.Declarations[DeclarationType.D390].Lines.ShouldHaveSingleItem();
        (row.OperationType, row.Base).ShouldBe(("S", 1000m));
    }

    /// <summary>Scenariul 4 și F16: două entități UE în aceeași lună → un total D301, două rânduri D390, Σ D390 = D301.</summary>
    [Fact]
    public void S4_F16_TwoEuEntitiesGiveOneD301TotalAndTwoD390Rows()
    {
        TaxResult result = MonthlyTaxEngine.Calculate(Input());

        result.Declarations[DeclarationType.D390].Lines.Count.ShouldBe(2);
        result.Declarations[DeclarationType.D390].Lines.Sum(line => line.Base).ShouldBe(result.Declarations[DeclarationType.D301].Lines.Sum(line => line.Base));
    }

    /// <summary>Scenariul 2 și F04: aceeași factură pe un PFA fără art. 317 activ → Stop.</summary>
    [Fact]
    public void S2_F04_WithoutArt317TheEuInvoiceStops()
    {
        TaxResult result = MonthlyTaxEngine.Calculate(Input() with { Art317 = [] });

        result.BlockingReasons.ShouldContain(reason => reason.StartsWith(MonthlyTaxEngine.Art317Missing, StringComparison.Ordinal));
    }

    [Fact]
    public void F18_S5_MonthWithoutEuInvoicesHasNoD301OrD390()
    {
        SupplierTaxProfile nonEu = UberProfile();
        nonEu.Country = "US";
        nonEu.VatId = "US123";

        TaxResult result = MonthlyTaxEngine.Calculate(Input(suppliers: [nonEu], invoices: [Uber(600m) with { SupplierVatId = "US123" }]));

        result.Declarations[DeclarationType.D301].Applicable.ShouldBeFalse();
        result.Declarations[DeclarationType.D390].Applicable.ShouldBeFalse();
        result.Declarations[DeclarationType.D100].Applicable.ShouldBeTrue();
    }

    [Fact]
    public void Month_without_invoices_is_not_applicable_anywhere() =>
        MonthlyTaxEngine.Calculate(Input(invoices: [])).Declarations.Values.ShouldAllBe(declaration => !declaration.Applicable);

    [Fact]
    public void Inactive_art317_blocks_the_eu_declarations()
    {
        PfaTaxInput input = Input() with { Art317 = [new Art317Period(true, new DateOnly(2025, 9, 1)), new Art317Period(false, new DateOnly(2026, 8, 1))] };

        MonthlyTaxEngine.Calculate(input).BlockingReasons.ShouldContain("Codul special de TVA art. 317 nu e activ la 31.08.2026.");
    }

    [Fact]
    public void Declaration_rounding_is_configurable_and_lines_keep_decimals()
    {
        PfaTaxInput input = Input(invoices: [Bolt(1000.40m)]);
        PfaTaxInput whole = input with
        {
            Settings = input.Settings with { Rounding = new Dictionary<DeclarationType, DeclarationRounding> { [DeclarationType.D301] = DeclarationRounding.WholeLei } },
        };

        MonthlyTaxEngine.Calculate(input).Declarations[DeclarationType.D301].Total.ShouldBe(210.08m);
        DeclarationCalculation rounded = MonthlyTaxEngine.Calculate(whole).Declarations[DeclarationType.D301];
        rounded.Total.ShouldBe(210m);
        rounded.Lines[0].Value.ShouldBe(210.08m);
    }

    [Fact]
    public void Disabled_or_unconfirmed_rent_rule_calculates_nothing()
    {
        PfaTaxInput input = Input() with
        {
            D100Rules = [.. Rules(), new D100Rule { Code = D100RuleCode.D100RentIndividual, Enabled = true, PendingConfirmation = true, ValidFrom = new DateOnly(2025, 1, 1) }],
        };

        MonthlyTaxEngine.Calculate(input).Declarations[DeclarationType.D100].Lines.ShouldAllBe(line => line.RuleCode == MonthlyTaxEngine.D100CommissionRule);
    }

    [Fact]
    public void Bnr_xml_is_parsed_with_multipliers()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <DataSet xmlns="http://www.bnr.ro/xsd">
              <Body><OrigCurrency>RON</OrigCurrency>
                <Cube date="2026-09-25"><Rate currency="EUR">4.9760</Rate><Rate currency="HUF" multiplier="100">1.2650</Rate></Cube>
              </Body>
            </DataSet>
            """;

        BnrExchangeRateImporter.Parse(xml).ShouldBe(
        [
            new BnrRate(new DateOnly(2026, 9, 25), "EUR", 4.9760m),
            new BnrRate(new DateOnly(2026, 9, 25), "HUF", 0.012650m),
        ]);
    }

    // ─── Date de test (spec §5.1) ──────────────────────────────────────────────────────────────

    private static SupplierTaxProfile BoltProfile() => new()
    {
        SupplierName = "Bolt Operations OÜ",
        Country = "EE",
        VatId = "EE102090374",
        Treaty = "Convenția RO–EE",
        D100Rate = 2,
        D100RateConfirmed = true,
        ValidFrom = new DateOnly(2025, 1, 1),
        ResidenceCertValidFrom = new DateOnly(2026, 1, 1),
        ResidenceCertValidTo = new DateOnly(2026, 12, 31),
    };

    private static SupplierTaxProfile UberProfile() => new()
    {
        SupplierName = "Uber B.V.",
        Country = "NL",
        VatId = "NL852071589B01",
        Treaty = "Convenția RO–NL",
        D100Rate = 0,
        D100RateConfirmed = true,
        ValidFrom = new DateOnly(2025, 1, 1),
        ResidenceCertValidFrom = new DateOnly(2026, 1, 1),
        ResidenceCertValidTo = new DateOnly(2026, 12, 31),
    };

    private static List<D100Rule> Rules() =>
    [
        new() { Code = D100RuleCode.D100CommissionNonresident, Enabled = true, ValidFrom = new DateOnly(2025, 1, 1) },
        new() { Code = D100RuleCode.D100RentIndividual, Enabled = false, PendingConfirmation = true, ValidFrom = new DateOnly(2025, 1, 1) },
    ];

    private static TaxInvoice Bolt(decimal commission) =>
        new(Guid.NewGuid(), "Factura Bolt", "EE102090374", "B-1", new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), "RON", commission, new DateOnly(2026, 8, 31));

    private static TaxInvoice Uber(decimal commission) =>
        new(Guid.NewGuid(), "Factura Uber", "NL852071589B01", "U-1", new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), "RON", commission, new DateOnly(2026, 8, 31));

    private static PfaTaxInput Input(IReadOnlyList<SupplierTaxProfile>? suppliers = null, IReadOnlyList<TaxInvoice>? invoices = null)
    {
        IReadOnlyList<TaxInvoice> list = invoices ?? [Bolt(1000m), Uber(600m)];
        IReadOnlyList<SupplierTaxProfile> profiles = suppliers ?? [BoltProfile(), UberProfile()];
        return new(
            "2026-08",
            list,
            [new TaxReport(Guid.NewGuid(), "RON", 8000m, new DateOnly(2026, 8, 31)), new TaxReport(Guid.NewGuid(), "RON", 5000m, new DateOnly(2026, 8, 31))],
            profiles,
            [new VatRate { Rate = 19, ValidFrom = new DateOnly(2017, 1, 1), ValidTo = new DateOnly(2025, 7, 31) }, new VatRate { Rate = 21, ValidFrom = new DateOnly(2025, 8, 1) }],
            Rules(),
            [],
            [new Art317Period(true, new DateOnly(2025, 9, 1))],
            TaxEngineSettings.ForPeriod(new TaxRuleSet(TaxRuleSeed.Rules), "2026-08"),
            Payments(list, profiles));
    }

    /// <summary>
    /// Plata fiecărei facturi (comisionul reținut la decontare), la data facturii sau la cea dată, cu
    /// decizia motorului nerezident pe regulile seed și pe registrul de furnizori.
    /// </summary>
    internal static List<NonResidentLine> Payments(IEnumerable<TaxInvoice> invoices, IReadOnlyList<SupplierTaxProfile> suppliers, DateOnly? paidOn = null, TaxRuleSet? rules = null) =>
        [.. invoices
            .Where(invoice => invoice.CommissionAmount is not null && suppliers.Any(s => string.Equals(s.VatId, invoice.SupplierVatId, StringComparison.OrdinalIgnoreCase)))
            .Select(invoice =>
            {
                SupplierTaxProfile supplier = suppliers.First(s => string.Equals(s.VatId, invoice.SupplierVatId, StringComparison.OrdinalIgnoreCase));
                var payment = new NonResidentPayment
                {
                    Id = Guid.NewGuid(), SupplierLegalName = supplier.SupplierName, SupplierCountry = supplier.Country, SupplierTaxId = supplier.VatId,
                    PaymentDate = paidOn ?? invoice.InvoiceDate!.Value, GrossIncomeRon = invoice.CommissionAmount!.Value, IncomeType = "COMMISSION",
                };
                NonResidentDecisionResult decision = NonResidentTaxEngine.Decide(payment, suppliers, rules ?? new TaxRuleSet(TaxRuleSeed.Rules));
                return new NonResidentLine(
                    payment.Id, invoice.DocumentId, invoice.Label, payment.PaymentDate, supplier.SupplierName, supplier.Country, supplier.VatId,
                    payment.GrossIncomeRon, decision.Rate, decision.TaxDue, decision.ObligationCode, decision.Status, decision.Explanation, invoice.Platform,
                    supplier.Treaty, supplier.ResidenceCertValidFrom, supplier.ResidenceCertValidTo);
            })];

    // ─── Formatul cazurilor golden ─────────────────────────────────────────────────────────────

    private sealed record GoldenCase(
        string Name,
        string Period,
        List<SupplierTaxProfile> Suppliers,
        List<GoldenVat> VatRates,
        List<GoldenArt317> Art317,
        List<ExchangeRate> ExchangeRates,
        List<GoldenInvoice> Invoices,
        List<GoldenReport> Reports,
        GoldenExpected Expected)
    {
        public PfaTaxInput ToInput() => new(
            Period,
            [.. Invoices.Select(i => new TaxInvoice(Guid.NewGuid(), i.Label, i.SupplierVatId, null, i.InvoiceDate, i.ServicePeriodEnd, i.Currency, i.CommissionAmount, i.TaxPointDate))],
            [.. Reports.Select(r => new TaxReport(Guid.NewGuid(), r.Currency, r.Income, r.PeriodTo))],
            Suppliers,
            [.. VatRates.Select(v => new VatRate { Rate = v.Rate, ValidFrom = v.ValidFrom, ValidTo = v.ValidTo })],
            Rules(),
            ExchangeRates,
            [.. Art317.Select(a => new Art317Period(a.Enabled, a.ValidFrom))],
            TaxEngineSettings.ForPeriod(new TaxRuleSet(TaxRuleSeed.Rules), "2026-08"),
            null);
    }

    private sealed record GoldenVat(decimal Rate, DateOnly ValidFrom, DateOnly? ValidTo);

    private sealed record GoldenArt317(bool Enabled, DateOnly ValidFrom);

    private sealed record GoldenInvoice(string Label, string SupplierVatId, DateOnly InvoiceDate, DateOnly? ServicePeriodEnd, string Currency, decimal CommissionAmount, DateOnly? TaxPointDate = null);

    private sealed record GoldenReport(string Currency, decimal Income, DateOnly? PeriodTo);

    private sealed record GoldenExpected(List<string> BlockingReasons, ExpectedDeclaration? D100, ExpectedDeclaration? D301, ExpectedDeclaration? D390)
    {
        public IEnumerable<(string Type, ExpectedDeclaration Declaration)> Declarations()
        {
            if (D100 is not null)
            {
                yield return ("D100", D100);
            }

            if (D301 is not null)
            {
                yield return ("D301", D301);
            }

            if (D390 is not null)
            {
                yield return ("D390", D390);
            }
        }
    }

    private sealed record ExpectedDeclaration(decimal Total, decimal? ExcludedRideIncome, List<ExpectedLine> Lines);

    private sealed record ExpectedLine(string SupplierVatId, decimal Base, decimal? Rate, decimal Value, string? OperationType, string? Country);
}
