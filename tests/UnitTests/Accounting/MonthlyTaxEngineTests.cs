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

        TaxResult result = MonthlyTaxEngine.Calculate(golden.ToInput());

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

        result.Declarations[DeclarationType.D100].Lines[0].Explanation.ShouldBe("1.000,00 × 2% = 20,00");
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

    [Fact]
    public void Expired_residence_certificate_blocks_d100()
    {
        SupplierTaxProfile expired = BoltProfile();
        expired.ResidenceCertValidTo = new DateOnly(2026, 7, 31);

        TaxResult result = MonthlyTaxEngine.Calculate(Input(suppliers: [expired, UberProfile()]));

        result.IsBlocked.ShouldBeTrue();
        result.BlockingReasons.ShouldContain("Certificatul de rezidență pentru Bolt Operations OÜ nu e valabil la 31.08.2026.");
    }

    [Fact]
    public void Unconfirmed_d100_rate_blocks()
    {
        SupplierTaxProfile uber = UberProfile();
        uber.D100RateConfirmed = false;

        MonthlyTaxEngine.Calculate(Input(suppliers: [BoltProfile(), uber])).BlockingReasons
            .ShouldBe(["Cota D100 pentru Uber B.V. nu e confirmată."]);
    }

    [Fact]
    public void Vat_rate_changed_mid_month_uses_the_exigibility_date()
    {
        VatRate[] rates =
        [
            new() { Rate = 19, ValidFrom = new DateOnly(2017, 1, 1), ValidTo = new DateOnly(2026, 8, 15) },
            new() { Rate = 21, ValidFrom = new DateOnly(2026, 8, 16) },
        ];
        TaxInvoice invoice = Bolt(1000m) with { ServicePeriodEnd = new DateOnly(2026, 8, 10) };

        PfaTaxInput byInvoiceDate = Input(invoices: [invoice]) with { VatRates = rates };
        PfaTaxInput byServicePeriod = byInvoiceDate with { Settings = byInvoiceDate.Settings with { VatExigibility = VatExigibilityRule.ServicePeriodEnd } };

        MonthlyTaxEngine.Calculate(byInvoiceDate).Declarations[DeclarationType.D301].Total.ShouldBe(210m);
        MonthlyTaxEngine.Calculate(byServicePeriod).Declarations[DeclarationType.D301].Total.ShouldBe(190m);
    }

    [Fact]
    public void Month_without_eu_invoices_is_not_applicable_for_d301_and_d390()
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
        VatId = "NL852071588B01",
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
        new(Guid.NewGuid(), "Factura Bolt", "EE102090374", "B-1", new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), "RON", commission);

    private static TaxInvoice Uber(decimal commission) =>
        new(Guid.NewGuid(), "Factura Uber", "NL852071588B01", "U-1", new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), "RON", commission);

    private static PfaTaxInput Input(IReadOnlyList<SupplierTaxProfile>? suppliers = null, IReadOnlyList<TaxInvoice>? invoices = null) => new(
        "2026-08",
        invoices ?? [Bolt(1000m), Uber(600m)],
        [new TaxReport(Guid.NewGuid(), "RON", 8000m, new DateOnly(2026, 8, 31)), new TaxReport(Guid.NewGuid(), "RON", 5000m, new DateOnly(2026, 8, 31))],
        suppliers ?? [BoltProfile(), UberProfile()],
        [new VatRate { Rate = 19, ValidFrom = new DateOnly(2017, 1, 1), ValidTo = new DateOnly(2025, 7, 31) }, new VatRate { Rate = 21, ValidFrom = new DateOnly(2025, 8, 1) }],
        Rules(),
        [],
        [new Art317Period(true, new DateOnly(2025, 9, 1))],
        TaxEngineSettings.From(new AccountingOptions()));

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
            [.. Invoices.Select(i => new TaxInvoice(Guid.NewGuid(), i.Label, i.SupplierVatId, null, i.InvoiceDate, i.ServicePeriodEnd, i.Currency, i.CommissionAmount))],
            [.. Reports.Select(r => new TaxReport(Guid.NewGuid(), r.Currency, r.Income, r.PeriodTo))],
            Suppliers,
            [.. VatRates.Select(v => new VatRate { Rate = v.Rate, ValidFrom = v.ValidFrom, ValidTo = v.ValidTo })],
            Rules(),
            ExchangeRates,
            [.. Art317.Select(a => new Art317Period(a.Enabled, a.ValidFrom))],
            TaxEngineSettings.From(new AccountingOptions()));
    }

    private sealed record GoldenVat(decimal Rate, DateOnly ValidFrom, DateOnly? ValidTo);

    private sealed record GoldenArt317(bool Enabled, DateOnly ValidFrom);

    private sealed record GoldenInvoice(string Label, string SupplierVatId, DateOnly InvoiceDate, DateOnly? ServicePeriodEnd, string Currency, decimal CommissionAmount);

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
