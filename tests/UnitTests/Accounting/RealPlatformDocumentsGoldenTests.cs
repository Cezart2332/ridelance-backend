using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Application.Accounting.Months;
using Application.Accounting.Tax;
using Domain.Accounting;
using Infrastructure.Accounting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// Cazul golden din documente reale, august 2026: factura lunară Bolt, cinci facturi săptămânale Uber,
/// rezumatul Bolt și sumarul fiscal Uber. Valorile de mai jos sunt transcrise din PDF-uri, exact cum
/// le-ar citi extracția.
/// <para>
/// Așteptat: bază D301 15.732,10 (Uber 13.458,87 după data impozitării + Bolt 2.273,23), D301 3.304 lei
/// (3.303,75 în XML, cu bani),
/// D100 Bolt 45 (2% din 2.273,23 = 45,46; rezumatul Bolt arată reținere 44,97 — diferența se
/// semnalează, nu se corectează).
/// </para>
/// <para>
/// PDF-urile nu sunt în repo (sunt documentele unui client). Când există local în
/// <c>test-data/real/</c>, testul verifică și că fiecare sumă transcrisă apare în textul lor.
/// </para>
/// </summary>
public sealed class RealPlatformDocumentsGoldenTests
{
    private const string Period = "2026-08";
    private const string BoltVatId = "EE102090374";
    private const string UberVatId = "NL852071589B01";

    private sealed record RealDocument(string FileName, Platform Platform, PlatformDocumentType Type, ExtractedFields Fields);

    private static ExtractedFields UberWeek(string number, DateOnly invoiceDate, DateOnly from, DateOnly to, decimal amount) =>
        new("Uber B.V.", "NL", UberVatId, number, invoiceDate, from, to, "RON", amount, amount, [], TaxPointDate: to);

    /// <summary>Documentele reale; numele fișierelor sunt cele primite.</summary>
    private static readonly RealDocument[] Documents =
    [
        // Factura Bolt nu tipărește data impozitării: contabilul o completează la confirmare (spec declarații F12).
        new("Factura Bolt - RO1126-153658-AV FLEET EXPERT SRL (3).pdf", Platform.Bolt, PlatformDocumentType.CommissionInvoice,
            new ExtractedFields("Bolt Operations OÜ", "EE", BoltVatId, "RO1126-153658", new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
                "RON", 2273.23m, 2273.23m, [], TaxPointDate: new DateOnly(2026, 8, 31))),
        new("9246aaf0-04f9-5cd3-9f71-7b438379a561_065c6979-55ae-5318-9e8b-aab5f4fa6f51.pdf", Platform.Uber, PlatformDocumentType.CommissionInvoice,
            UberWeek("UBERDEF-GGCDIBAJ-01-2026-0000033", new DateOnly(2026, 8, 5), new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2), 3611.83m)),
        new("95ff5bec-f95a-5489-afa7-2bf6c20b9422_ac7e1896-b0a0-58fa-bcbd-69e84b34a655.pdf", Platform.Uber, PlatformDocumentType.CommissionInvoice,
            UberWeek("UBERDEF-GGCDIBAJ-01-2026-0000034", new DateOnly(2026, 8, 11), new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9), 2718.70m)),
        new("fb0ed382-0ba8-5133-a3b5-85b728adcf1a_4d7c2581-2856-5856-8fb3-9492cd77b4b7.pdf", Platform.Uber, PlatformDocumentType.CommissionInvoice,
            UberWeek("UBERDEF-GGCDIBAJ-01-2026-0000035", new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16), 1797.22m)),
        new("9cb89616-1091-5335-9bfa-c1e86f4e7cba_ca5efb72-2662-53b5-97d5-87c71b1c8101.pdf", Platform.Uber, PlatformDocumentType.CommissionInvoice,
            UberWeek("UBERDEF-GGCDIBAJ-01-2026-0000036", new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 23), 2795.46m)),
        // Emisă pe 03.09, dar data impozitării e 30.08: intră în august.
        new("4ac7f082-ab1e-5279-8b7a-ec244c6de33a_59ee5630-e3c9-57d4-a0df-e2eafdc9a855.pdf", Platform.Uber, PlatformDocumentType.CommissionInvoice,
            UberWeek("UBERDEF-GGCDIBAJ-01-2026-0000037", new DateOnly(2026, 9, 3), new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 30), 2535.66m)),
        new("Raport fiscal - 2026-08-01 - 2026-08-31-AV FLEET EXPERT SRL (1).pdf", Platform.Bolt, PlatformDocumentType.PlatformReport,
            new ExtractedFields("Bolt Operations OÜ", "EE", null, null, null, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
                "RON", 20758.20m, 2273.23m, [], WithheldTax: 44.97m)),
        new("2026 August Monthly Summary (2).pdf", Platform.Uber, PlatformDocumentType.PlatformReport,
            new ExtractedFields("Uber B.V.", "NL", UberVatId, null, null, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
                "RON", 41737.00m, 9973.50m, [])),
    ];

    private static readonly SupplierTaxProfile[] Suppliers =
    [
        new() { SupplierName = "Bolt Operations OÜ", Country = "EE", VatId = BoltVatId, Treaty = "Convenția RO–EE", D100Rate = 2, D100RateConfirmed = true,
                ValidFrom = new DateOnly(2025, 1, 1), ResidenceCertValidFrom = new DateOnly(2026, 1, 1), ResidenceCertValidTo = new DateOnly(2026, 12, 31) },
        new() { SupplierName = "Uber B.V.", Country = "NL", VatId = UberVatId, Treaty = "Convenția RO–NL", D100Rate = 0, D100RateConfirmed = true,
                ValidFrom = new DateOnly(2025, 1, 1), ResidenceCertValidFrom = new DateOnly(2026, 1, 1), ResidenceCertValidTo = new DateOnly(2026, 12, 31) },
    ];

    private static IEnumerable<(RealDocument Document, Guid Id)> Invoices() =>
        Documents.Where(d => d.Type == PlatformDocumentType.CommissionInvoice).Select(d => (d, Guid.NewGuid()));

    private static PfaTaxInput Input(IEnumerable<(RealDocument Document, Guid Id)> invoices) => new(
        Period,
        [.. invoices.Select(item => new TaxInvoice(
            item.Id, item.Document.Fields.InvoiceNumber!, item.Document.Fields.SupplierVatId, item.Document.Fields.InvoiceNumber, item.Document.Fields.InvoiceDate,
            item.Document.Fields.PeriodTo, item.Document.Fields.Currency, item.Document.Fields.CommissionAmount, item.Document.Fields.TaxPointDate, item.Document.Platform))],
        [.. Documents.Where(d => d.Type == PlatformDocumentType.PlatformReport).Select(d => new TaxReport(
            Guid.NewGuid(), d.Fields.Currency, d.Fields.Amount, d.Fields.PeriodTo, d.Platform, d.Fields.CommissionAmount, d.Fields.WithheldTax))],
        Suppliers,
        [new VatRate { Rate = 19, ValidFrom = new DateOnly(2017, 1, 1), ValidTo = new DateOnly(2025, 7, 31) }, new VatRate { Rate = 21, ValidFrom = new DateOnly(2025, 8, 1) }],
        [
            new D100Rule { Code = D100RuleCode.D100CommissionNonresident, Enabled = true, ValidFrom = new DateOnly(2025, 1, 1) },
            new D100Rule { Code = D100RuleCode.D100RentIndividual, Enabled = false, PendingConfirmation = true, ValidFrom = new DateOnly(2025, 1, 1) },
        ],
        [],
        [new Art317Period(true, new DateOnly(2025, 9, 1))],
        TaxEngineSettings.ForPeriod(new TaxRuleSet(TaxRuleSeed.Rules), "2026-08"));

    [Fact]
    public void August_2026_real_documents_give_the_expected_declarations()
    {
        TaxResult result = MonthlyTaxEngine.Calculate(Input(Invoices()));

        result.BlockingReasons.ShouldBeEmpty();
        DeclarationCalculation d301 = result.Declarations[DeclarationType.D301];
        d301.Lines.Sum(line => line.Base).ShouldBe(15732.10m);
        // D301 e în bani (structura ANAF: N(15.2), tva4 = suma liniilor), TVA pe fiecare factură:
        // 3.303,75; de plată, rotunjit, 3.304 lei.
        d301.Total.ShouldBe(3303.75m);
        Math.Round(d301.Total, 0, MidpointRounding.AwayFromZero).ShouldBe(3304m);
        d301.ExcludedRideIncome.ShouldBe(62495.20m);

        DeclarationCalculation d100 = result.Declarations[DeclarationType.D100];
        d100.Total.ShouldBe(45m);
        d100.Lines.Single(line => line.SupplierVatId == BoltVatId).Value.ShouldBe(45.46m);

        result.Declarations[DeclarationType.D390].Lines.Select(line => (line.SupplierVatId, line.Base))
            .ShouldBe([(BoltVatId, 2273.23m), (UberVatId, 13458.87m)]);
    }

    [Fact]
    public void Platform_withholding_and_report_correlation_are_warnings_not_blocks()
    {
        TaxResult result = MonthlyTaxEngine.Calculate(Input(Invoices()));

        DeclarationCalculation d100 = result.Declarations[DeclarationType.D100];
        d100.Withholding.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(new WithholdingComparison("Bolt", 44.97m, 45.46m));
        d100.Warnings.ShouldBe(["Bolt: reținerea la sursă raportată e 44,97 lei, D100 calculat 45,46 lei (diferență 0,49 lei). Nu se corectează automat."]);

        // Sumarul Uber e pe lună calendaristică; facturile săptămânale, pe săptămânile lor.
        result.Declarations[DeclarationType.D301].Warnings.ShouldBe(
        [
            "Uber: facturile de comision din lună însumează 13.458,87 lei, raportul arată comision 9.973,50 lei. " +
            "Raportul poate acoperi altă perioadă decât facturile; diferența nu blochează.",
        ]);
        result.IsBlocked.ShouldBeFalse();
    }

    [Fact]
    public void Five_weekly_uber_invoices_cover_august_and_a_missing_week_is_named()
    {
        List<MonthDocument> uber = [.. Invoices().Where(i => i.Document.Platform == Platform.Uber).Select(i => MonthDocumentOf(i.Document))];

        PreCheck.InvoiceCoverageGaps(Period, Platform.Uber, uber).ShouldBeEmpty();

        PreCheck.InvoiceCoverageGaps(Period, Platform.Uber, [.. uber.Where(d => d.Extraction!.InvoiceNumber != "UBERDEF-GGCDIBAJ-01-2026-0000035")])
            .ShouldBe(["Lipsește factura de comision Uber pentru 10.08.2026–16.08.2026."]);
        PreCheck.InvoiceCoverageGaps(Period, Platform.Uber, [.. uber.Where(d => d.Extraction!.InvoiceNumber != "UBERDEF-GGCDIBAJ-01-2026-0000037")])
            .ShouldBe(["Lipsește factura de comision Uber pentru perioada de după 23.08.2026."]);
        PreCheck.InvoiceCoverageGaps(Period, Platform.Uber, [.. uber.Where(d => d.Extraction!.InvoiceNumber != "UBERDEF-GGCDIBAJ-01-2026-0000033")])
            .ShouldBe(["Lipsește factura de comision Uber pentru perioada de dinainte de 03.08.2026."]);

        MonthDocument bolt = MonthDocumentOf(Documents[0]);
        PreCheck.InvoiceCoverageGaps(Period, Platform.Bolt, [bolt]).ShouldBeEmpty();
    }

    [Fact]
    public void Every_real_document_passes_its_checks_for_august()
    {
        Dictionary<string, string?> texts = RealPdfTexts();
        // Cu folderul local, toate PDF-urile trebuie citite (au text layer), altfel testul ar sări tăcut peste sume.
        texts.Where(pair => pair.Value is null).Select(pair => pair.Key).ShouldBeEmpty();
        foreach (RealDocument document in Documents)
        {
            decimal? reportIncome = document.Type == PlatformDocumentType.CommissionInvoice
                ? Documents.Single(d => d.Platform == document.Platform && d.Type == PlatformDocumentType.PlatformReport).Fields.Amount
                : null;
            // Fără PDF-uri locale, sumele nu se pot căuta în text; restul verificărilor rulează oricum.
            string? text = texts.GetValueOrDefault(document.FileName);
            IReadOnlyList<DocumentCheck> checks = DocumentChecker.Run(
                new CheckSubject(Period, document.Type, text),
                document.Fields,
                new CheckContext(Suppliers, false, null, reportIncome, new AccountingOptions()));

            checks.Where(check => !check.Passed && (text is not null || check.Code != DocumentCheckCode.AmountInText))
                .Select(check => $"{document.FileName}: {check.Message}")
                .ShouldBeEmpty();
            checks.Where(check => check.Warning).ShouldBeEmpty(document.FileName);
        }
    }

    [Theory]
    [InlineData("2273.23", 2273.23)]
    [InlineData("2.535,66", 2535.66)]
    [InlineData("41.737,00 RON", 41737.00)]
    [InlineData("1,248.50", 1248.50)]
    [InlineData("44,97", 44.97)]
    [InlineData("2.535", 2535)]
    public void Amounts_are_read_in_both_notations(string text, double expected) =>
        AmountText.Parse(text).ShouldBe((decimal)expected);

    private static MonthDocument MonthDocumentOf(RealDocument document)
    {
        var platformDocument = new PlatformDocument { Id = Guid.NewGuid(), Period = Period, Platform = document.Platform, DocumentType = document.Type, Status = PlatformDocumentStatus.Confirmed };
        var extraction = new DocumentExtraction { Id = Guid.NewGuid(), PlatformDocumentId = platformDocument.Id, IsCurrent = true };
        PlatformDocumentSupport.Apply(extraction, document.Fields);
        return new MonthDocument(platformDocument, document.FileName, extraction);
    }

    /// <summary>Textul PDF-urilor din <c>test-data/real/</c> (lângă backend), dacă există local.</summary>
    private static Dictionary<string, string?> RealPdfTexts()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string folder = Path.Combine(directory.FullName, "test-data", "real");
            if (Directory.Exists(folder))
            {
                var extractor = new PdfPigTextExtractor(NullLogger<PdfPigTextExtractor>.Instance);
                return Documents.ToDictionary(
                    document => document.FileName,
                    document => File.Exists(Path.Combine(folder, document.FileName))
                        ? extractor.ExtractText(File.ReadAllBytes(Path.Combine(folder, document.FileName)))
                        : null);
            }
        }

        return [];
    }
}
