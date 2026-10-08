using Application.Abstractions.Ai;
using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Domain.Accounting;
using Infrastructure.Accounting;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// Rezumatul lunar Bolt. Bugul: câmpul „Venituri totale înainte de comision” rămânea gol, fiindcă
/// raportul Bolt nu are un rând cu numele ăsta — brutul e în două rânduri TOTAL —, iar modelul nu
/// are voie să adune. Fără brut, venitul lunii nu ajungea în RJIP și REF.
/// Cifrele sunt cele din rezumatul din septembrie 2026.
/// </summary>
public sealed class BoltReportTests
{
    private const string ReportText =
        "Rezumat lunar pentru perioada 01.09.2026 - 30.09.2026\n" +
        "DEFALCARE TARIF (INC. TVA)\nTarif cursă (numerar) 7.540,50 lei\nTarif cursă (în aplicație) 15.866,40 lei\n" +
        "Taxă de anulare 69,00 lei\nTaxă de drum 0,00 lei\nTaxă de rezervare 30,00 lei\nBacșiș 249,00 lei\nTOTAL 23.754,90 lei\n" +
        "DEFALCARE ALTE VENITURI\nBonusuri 0,00 lei\nAlte compensări 98,85 lei\nTOTAL 98,85 lei\n" +
        "DEDUCERI\nComision Bolt după deduceri 2.149,09 lei\nRambursări clienți 0,00 lei\n" +
        "DEFALCARE TVA\nTOTAL 0,00 lei\nReținere la sursă 42,77 lei";

    private const string ModelResponse = """
        {
          "document_type": "PLATFORM_REPORT",
          "platform": "BOLT",
          "fields": {
            "supplier_name": "Bolt", "supplier_country": null, "supplier_vat_id": null, "invoice_number": null,
            "invoice_date": "2026-10-01", "period_from": "2026-09-01", "period_to": "2026-09-30", "currency": "RON",
            "amount": null, "commission_amount": 2149.09, "tax_point_date": null, "withheld_tax": 42.77, "cash_amount": 7540.50,
            "fare_total": 23754.90, "other_income_total": 98.85, "customer_refunds": 0,
            "other_amounts": []
          },
          "source_snippets": {
            "supplier_name": null, "supplier_country": null, "supplier_vat_id": null, "invoice_number": null, "invoice_date": null,
            "period_from": null, "period_to": null, "currency": null, "amount": null, "commission_amount": "2.149,09",
            "tax_point_date": null, "withheld_tax": "42,77", "cash_amount": "7.540,50",
            "fare_total": "23.754,90", "other_income_total": "98,85", "customer_refunds": "0,00"
          },
          "confidence": 0.95,
          "customer_name": null,
          "customer_tax_id": null
        }
        """;

    private static ExtractedFields Read() =>
        OpenRouterDocumentExtractor.Parse(ModelResponse, "model", "v3").Value.Fields;

    private static IReadOnlyList<DocumentCheck> Check(ExtractedFields fields, string text = ReportText) =>
        DocumentChecker.Run(
            new CheckSubject("2026-09", PlatformDocumentType.PlatformReport, text),
            fields,
            new CheckContext([], false, null, null, new AccountingOptions()));

    /// <summary>Brutul e TOTAL-ul de la „Defalcare tarif”, exact ca în raport; alte venituri rămân separat.</summary>
    [Fact]
    public void The_gross_is_the_fare_total()
    {
        ExtractedFields fields = Read();

        fields.Amount.ShouldBe(23754.90m);
        ReportComponents.OtherIncome(fields).ShouldBe(98.85m);
        ReportComponents.Of(fields, ReportComponents.FareTotal).ShouldBe(23754.90m);
        ReportComponents.Of(fields, ReportComponents.OtherIncomeTotal).ShouldBe(98.85m);
        ReportComponents.Refunds(fields).ShouldBe(0m);
        fields.CashAmount.ShouldBe(7540.50m);
        fields.WithheldTax.ShouldBe(42.77m);
    }

    [Fact]
    public void Component_snippets_are_kept_for_highlighting()
    {
        Result<DocumentExtractionResult> result = OpenRouterDocumentExtractor.Parse(ModelResponse, "model", "v3");

        result.Value.SourceSnippets["fareTotal"].ShouldBe("23.754,90");
        result.Value.SourceSnippets["otherIncomeTotal"].ShouldBe("98,85");
    }

    /// <summary>Brutul compus nu apare ca atare în PDF; componentele lui da, și ele se verifică.</summary>
    [Fact]
    public void A_composed_report_passes_every_check() =>
        Check(Read()).Where(check => !check.Passed).ShouldBeEmpty();

    [Fact]
    public void A_report_without_gross_cannot_be_confirmed()
    {
        DocumentCheck arithmetic = Check(Read() with { Amount = null, OtherAmounts = [] })
            .Single(check => check.Code == DocumentCheckCode.Arithmetic);

        arithmetic.Passed.ShouldBeFalse();
        arithmetic.Message.ShouldContain("Venitul brut");
    }

    [Fact]
    public void A_gross_that_is_not_the_fare_total_fails()
    {
        DocumentCheck arithmetic = Check(Read() with { Amount = 23853.75m })
            .Single(check => check.Code == DocumentCheckCode.Arithmetic);

        arithmetic.Passed.ShouldBeFalse();
    }

    [Fact]
    public void A_component_missing_from_the_pdf_text_fails()
    {
        DocumentCheck inText = Check(Read(), ReportText.Replace("98,85", "98,00", StringComparison.Ordinal))
            .Single(check => check.Code == DocumentCheckCode.AmountInText);

        inText.Passed.ShouldBeFalse();
    }

    /// <summary>Uber are „Venituri totale” scris pe raport: rămâne citit direct, fără componente.</summary>
    [Fact]
    public void An_uber_total_is_kept_as_read()
    {
        var uber = new ExtractedFields(
            "Uber", "NL", null, null, null, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "RON", 42582.00m, 9973.50m, []);

        ReportComponents.WithGross(uber).ShouldBe(uber);
        Check(uber, "Venituri totale 42.582,00 RON Comision 9.973,50")
            .Where(check => !check.Passed && check.Code is DocumentCheckCode.Arithmetic or DocumentCheckCode.AmountInText)
            .ShouldBeEmpty();
    }
}
