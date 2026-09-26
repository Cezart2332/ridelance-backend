using System.Text.Json;
using Application.Abstractions.Ai;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Infrastructure.Accounting;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>Piesele reale ale extracției (fără rețea): PdfPig, schema strictă, interpretarea răspunsului.</summary>
public sealed class DocumentExtractionInfrastructureTests
{
    [Fact]
    public void PdfPig_reads_the_text_layer_with_amounts_intact()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        byte[] pdf = Document.Create(container => container.Page(page =>
            page.Content().Column(column =>
            {
                column.Item().Text("Bolt Operations OÜ");
                column.Item().Text("Comision servicii platformă: 1.248,50 RON");
                column.Item().Text("Total de plată: 1.248,50 RON");
            }))).GeneratePdf();

        string? text = new PdfPigTextExtractor(NullLogger<PdfPigTextExtractor>.Instance).ExtractText(pdf);

        text.ShouldNotBeNull();
        text.ShouldContain("1.248,50");
        text.ShouldContain("Bolt Operations");
    }

    [Fact]
    public void PdfPig_returns_null_for_a_file_that_is_not_a_pdf() =>
        new PdfPigTextExtractor(NullLogger<PdfPigTextExtractor>.Instance).ExtractText([1, 2, 3]).ShouldBeNull();

    [Fact]
    public void Model_response_is_mapped_to_the_contract()
    {
        const string json = """
            {
              "document_type": "COMMISSION_INVOICE",
              "platform": "BOLT",
              "fields": {
                "supplier_name": "Bolt Operations OÜ", "supplier_country": "ee", "supplier_vat_id": "EE 102090374",
                "invoice_number": "EE-BOLT-2026-08-1000", "invoice_date": "2026-08-31", "period_from": "2026-08-01",
                "period_to": "2026-08-31T00:00:00", "currency": "ron", "amount": 1000, "commission_amount": "1000.00",
                "other_amounts": [{ "label": "TVA", "amount": 0 }]
              },
              "source_snippets": { "supplier_name": "Bolt Operations OÜ", "commission_amount": "1.000,00", "amount": null,
                "supplier_country": null, "supplier_vat_id": null, "invoice_number": null, "invoice_date": null,
                "period_from": null, "period_to": null, "currency": null },
              "confidence": 0.93
            }
            """;

        Result<DocumentExtractionResult> result = OpenRouterDocumentExtractor.Parse(json, "model", "v1");

        result.IsSuccess.ShouldBeTrue();
        DocumentExtractionResult value = result.Value;
        value.DocumentType.ShouldBe(PlatformDocumentType.CommissionInvoice);
        value.Platform.ShouldBe(Platform.Bolt);
        value.Fields.ShouldBe(new ExtractedFields(
            "Bolt Operations OÜ", "EE", "EE102090374", "EE-BOLT-2026-08-1000", new DateOnly(2026, 8, 31),
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "RON", 1000m, 1000m, value.Fields.OtherAmounts));
        value.Fields.OtherAmounts.ShouldBe([new OtherAmount("TVA", 0)]);
        // Cheile fragmentelor sunt cele din contract (camelCase), ca în ecranul de verificare.
        value.SourceSnippets.ShouldBe(new Dictionary<string, string> { ["supplierName"] = "Bolt Operations OÜ", ["commissionAmount"] = "1.000,00" });
        value.Confidence.ShouldBe(0.93);
    }

    [Fact]
    public void Unreadable_model_response_is_a_failure() =>
        OpenRouterDocumentExtractor.Parse("nu e json", "model", "v1").IsFailure.ShouldBeTrue();

    [Fact]
    public void Schema_is_strict_every_property_required_and_closed()
    {
        using var schema = JsonDocument.Parse(JsonSerializer.Serialize(OpenRouterDocumentExtractor.Schema()));

        AssertStrict(schema.RootElement);
        AssertStrict(schema.RootElement.GetProperty("properties").GetProperty("fields"));
        AssertStrict(schema.RootElement.GetProperty("properties").GetProperty("source_snippets"));
    }

    private static void AssertStrict(JsonElement objectSchema)
    {
        objectSchema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        string[] properties = [.. objectSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name)];
        string[] required = [.. objectSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()!)];
        required.ShouldBe(properties, ignoreOrder: true);
    }
}
