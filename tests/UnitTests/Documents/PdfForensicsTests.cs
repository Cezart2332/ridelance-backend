using System.Text.Json;
using Application.Abstractions.Ai;
using Infrastructure.Ai;
using Infrastructure.Dossiers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace UnitTests.Documents;

/// <summary>
/// Ce spune un PDF despre el însuși. O adeverință scrisă în Word și exportată arată, pentru un
/// model care citește pagina, la fel de „oficial” ca una scanată; metadatele o deosebesc.
/// </summary>
public sealed class PdfForensicsTests
{
    /// <summary>Un PNG de 1×1 pixeli: imaginea minimă care face din pagină un „scan”.</summary>
    private static readonly byte[] Pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly PdfForensics Forensics = new(NullLogger<PdfForensics>.Instance);

    private static byte[] Pdf(string creator, bool withImage)
    {
        using var builder = new PdfDocumentBuilder();
        builder.DocumentInformation.Creator = creator;
        builder.DocumentInformation.Producer = creator;
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        PdfPageBuilder page = builder.AddPage(595, 842);
        page.AddText("ADEVERINTA MEDICALA - apt", 12, new PdfPoint(50, 750), font);
        if (withImage)
        {
            page.AddPng(Pixel, new PdfRectangle(50, 400, 300, 700));
        }

        return builder.Build();
    }

    [Fact]
    public void A_word_export_is_recognised()
    {
        PdfForensicsReport? report = Forensics.Inspect(Pdf("Microsoft® Word for Microsoft 365", withImage: false), "application/pdf");

        report.ShouldNotBeNull();
        report.FromTextEditor.ShouldBeTrue();
        report.HasImages.ShouldBeFalse();
        report.HasDigitalSignature.ShouldBeFalse();
    }

    [Fact]
    public void A_scan_has_an_image()
    {
        PdfForensicsReport? report = Forensics.Inspect(Pdf("Scanner App", withImage: true), "application/pdf");

        report.ShouldNotBeNull();
        report.HasImages.ShouldBeTrue();
        report.FromTextEditor.ShouldBeFalse();
    }

    [Fact]
    public void Photos_and_broken_files_say_nothing()
    {
        Forensics.Inspect([0xFF, 0xD8, 0xFF], "image/jpeg").ShouldBeNull();
        Forensics.Inspect([1, 2, 3], "application/pdf").ShouldBeNull();
    }

    [Fact]
    public void The_authenticity_block_is_parsed()
    {
        using var json = JsonDocument.Parse("""
            {"authenticity": {"letterhead": false, "stamp": true, "signature": null,
             "is_blank_template": false, "appears_self_made": true,
             "suspicion_reasons": ["Text pe foaie albă", ""]}}
            """);

        DocumentAuthenticityReport? report = OpenRouterDocumentAiAnalyzer.ParseAuthenticity(json.RootElement);

        report.ShouldNotBeNull();
        report.Letterhead.ShouldBe(false);
        report.Stamp.ShouldBe(true);
        report.Signature.ShouldBeNull();
        report.AppearsSelfMade.ShouldBe(true);
        report.SuspicionReasons.ShouldHaveSingleItem().ShouldBe("Text pe foaie albă");
    }

    [Fact]
    public void A_missing_authenticity_block_is_unknown()
    {
        using var json = JsonDocument.Parse("""{"matches_expected_type": true}""");

        OpenRouterDocumentAiAnalyzer.ParseAuthenticity(json.RootElement).ShouldBeNull();
    }
}
