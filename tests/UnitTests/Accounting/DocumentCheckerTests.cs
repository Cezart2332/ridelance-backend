using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Domain.Accounting;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>Verificările deterministe din B1, câte una: trece pe cazul bun, pică pe cel rău.</summary>
public sealed class DocumentCheckerTests
{
    private const string BoltInvoiceText =
        "Bolt Operations OÜ\nCod TVA: EE102090374\nFactură nr. EE-BOLT-2026-08-1000\nComision: 1.000,00 RON\nTVA: 0,00 RON\nTotal de plată: 1.000,00 RON";

    private static readonly SupplierTaxProfile Bolt = new()
    {
        Id = Guid.NewGuid(),
        SupplierName = "Bolt Operations OÜ",
        Country = "EE",
        VatId = "EE102090374",
        D100Rate = 2,
        D100RateConfirmed = true,
        ValidFrom = new DateOnly(2025, 1, 1),
    };

    private static ExtractedFields Invoice(decimal commission = 1000m, decimal? total = null, decimal vat = 0m) => new(
        "Bolt Operations OÜ",
        "EE",
        "EE102090374",
        "EE-BOLT-2026-08-1000",
        new DateOnly(2026, 8, 31),
        new DateOnly(2026, 8, 1),
        new DateOnly(2026, 8, 31),
        "RON",
        total ?? commission + vat,
        commission,
        [new OtherAmount("TVA", vat)]);

    private static CheckContext Context(
        IReadOnlyList<SupplierTaxProfile>? suppliers = null,
        bool duplicate = false,
        string? declaredElsewhere = null,
        decimal? reportIncome = 8000m,
        AccountingOptions? options = null) =>
        new(suppliers ?? [Bolt], duplicate, declaredElsewhere, reportIncome, options ?? new AccountingOptions());

    private static IReadOnlyList<DocumentCheck> RunInvoice(ExtractedFields fields, CheckContext? context = null, string? text = BoltInvoiceText) =>
        DocumentChecker.Run(new CheckSubject("2026-08", PlatformDocumentType.CommissionInvoice, text), fields, context ?? Context());

    private static DocumentCheck Check(IEnumerable<DocumentCheck> checks, DocumentCheckCode code) => checks.Single(check => check.Code == code);

    [Fact]
    public void Clean_invoice_passes_every_check()
    {
        IReadOnlyList<DocumentCheck> checks = RunInvoice(Invoice());

        checks.Select(check => check.Code).ShouldBe(
        [
            DocumentCheckCode.AmountInText, DocumentCheckCode.Arithmetic, DocumentCheckCode.SupplierKnown, DocumentCheckCode.VatIdFormat,
            DocumentCheckCode.NotDuplicate, DocumentCheckCode.NotAlreadyDeclared, DocumentCheckCode.PeriodMatch, DocumentCheckCode.CurrencyAllowed,
            DocumentCheckCode.SettlementCorrelation,
        ]);
        checks.ShouldAllBe(check => check.Passed);
        DocumentChecker.AllPassed(checks).ShouldBeTrue();
    }

    [Theory]
    [InlineData(1248.5, "1.248,50", "1,248.50", "1248.50", "1248,50")]
    [InlineData(20, "20,00", "20.00", "20.00", "20,00")]
    public void Amounts_are_searched_in_all_spellings(decimal value, string ro, string en, string plain, string plainComma) =>
        DocumentChecker.AmountSpellings(value).ShouldBe([ro, en, plain, plainComma]);

    [Fact]
    public void Amount_in_text_fails_when_the_model_misread_a_sum()
    {
        // Bogdan Matei: modelul a citit 1.284,50, în PDF scrie 1.248,50.
        string text = BoltInvoiceText.Replace("1.000,00", "1.248,50", StringComparison.Ordinal);

        DocumentCheck check = Check(RunInvoice(Invoice(1284.50m), text: text), DocumentCheckCode.AmountInText);

        check.Passed.ShouldBeFalse();
        check.Message.ShouldContain("Suma 1.284,50 (comision) nu apare în textul documentului.");
    }

    [Fact]
    public void Amount_in_text_fails_without_a_text_layer()
    {
        DocumentCheck check = Check(RunInvoice(Invoice(), text: null), DocumentCheckCode.AmountInText);

        check.Passed.ShouldBeFalse();
        check.Message.ShouldStartWith("PDF-ul nu are text");
    }

    [Fact]
    public void Arithmetic_fails_when_total_does_not_add_up() =>
        Check(RunInvoice(Invoice(1000m, total: 1100m)), DocumentCheckCode.Arithmetic).Passed.ShouldBeFalse();

    [Fact]
    public void Arithmetic_fails_when_the_invoice_charges_vat()
    {
        DocumentCheck check = Check(RunInvoice(Invoice(1000m, vat: 210m)), DocumentCheckCode.Arithmetic);

        check.Passed.ShouldBeFalse();
        check.Message.ShouldContain("taxare inversă");
    }

    [Fact]
    public void Supplier_known_fails_for_an_unknown_vat_id_and_offers_to_add_it()
    {
        // Răzvan Ene: cod TVA Uber necunoscut.
        DocumentCheck check = Check(RunInvoice(Invoice() with { SupplierVatId = "NL001234567B01", SupplierCountry = "NL" }), DocumentCheckCode.SupplierKnown);

        check.Passed.ShouldBeFalse();
        check.Action.ShouldBe(DocumentCheckAction.AddSupplier);
    }

    [Fact]
    public void Supplier_known_fails_when_the_profile_is_not_valid_at_the_invoice_date()
    {
        SupplierTaxProfile closed = new()
        {
            Id = Bolt.Id,
            SupplierName = Bolt.SupplierName,
            Country = Bolt.Country,
            VatId = Bolt.VatId,
            ValidFrom = Bolt.ValidFrom,
            ValidTo = new DateOnly(2026, 7, 31),
        };

        Check(RunInvoice(Invoice(), Context([closed])), DocumentCheckCode.SupplierKnown).Passed.ShouldBeFalse();
    }

    [Fact]
    public void Vat_id_format_fails_when_the_prefix_does_not_match_the_country() =>
        Check(RunInvoice(Invoice() with { SupplierCountry = "NL" }), DocumentCheckCode.VatIdFormat).Passed.ShouldBeFalse();

    [Fact]
    public void Period_match_fails_for_an_invoice_from_another_month() =>
        Check(RunInvoice(Invoice() with { InvoiceDate = new DateOnly(2026, 9, 1) }), DocumentCheckCode.PeriodMatch).Passed.ShouldBeFalse();

    [Fact]
    public void Period_match_follows_the_configured_exigibility_rule()
    {
        ExtractedFields fields = Invoice() with { InvoiceDate = new DateOnly(2026, 9, 1) };
        var servicePeriodEnd = new AccountingOptions { VatExigibility = VatExigibilityRule.ServicePeriodEnd };

        Check(RunInvoice(fields, Context(options: servicePeriodEnd)), DocumentCheckCode.PeriodMatch).Passed.ShouldBeTrue();
    }

    [Fact]
    public void Not_duplicate_fails_when_the_same_supplier_and_number_exist() =>
        Check(RunInvoice(Invoice(), Context(duplicate: true)), DocumentCheckCode.NotDuplicate).Passed.ShouldBeFalse();

    [Fact]
    public void Not_already_declared_fails_when_an_accepted_declaration_includes_it()
    {
        DocumentCheck check = Check(RunInvoice(Invoice(), Context(declaredElsewhere: "D301 pentru 2026-07")), DocumentCheckCode.NotAlreadyDeclared);

        check.Passed.ShouldBeFalse();
        check.Message.ShouldContain("D301 pentru 2026-07");
    }

    [Fact]
    public void Currency_allowed_fails_outside_ron_and_eur() =>
        Check(RunInvoice(Invoice() with { Currency = "USD" }), DocumentCheckCode.CurrencyAllowed).Passed.ShouldBeFalse();

    [Fact]
    public void Settlement_correlation_fails_outside_the_configured_range()
    {
        DocumentCheck check = Check(RunInvoice(Invoice(), Context(reportIncome: 2000m)), DocumentCheckCode.SettlementCorrelation);

        check.Passed.ShouldBeFalse();
        check.Message.ShouldContain("50,00%");
    }

    [Fact]
    public void Settlement_correlation_waits_for_the_report() =>
        Check(RunInvoice(Invoice(), Context(reportIncome: null)), DocumentCheckCode.SettlementCorrelation).Passed.ShouldBeTrue();

    [Fact]
    public void Reports_skip_the_invoice_only_checks()
    {
        ExtractedFields report = Invoice() with { InvoiceNumber = null, Amount = 8000m, OtherAmounts = [] };
        IReadOnlyList<DocumentCheck> checks = DocumentChecker.Run(
            new CheckSubject("2026-08", PlatformDocumentType.PlatformReport, "Venituri: 8.000,00\nComision: 1.000,00"),
            report,
            Context());

        checks.Select(check => check.Code).ShouldBe(
            [DocumentCheckCode.AmountInText, DocumentCheckCode.PeriodMatch, DocumentCheckCode.CurrencyAllowed, DocumentCheckCode.SettlementCorrelation]);
        checks.ShouldAllBe(check => check.Passed);
    }
}
