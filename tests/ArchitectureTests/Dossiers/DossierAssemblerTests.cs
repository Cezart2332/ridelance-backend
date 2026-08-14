using Application.Abstractions.Dossiers;
using Infrastructure.Dossiers;
using PdfSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Shouldly;
using Xunit;

namespace ArchitectureTests.Dossiers;

/// <summary>
/// RL-08 — dosarul trebuie să iasă uniform, indiferent ce a încărcat șoferul.
///
/// Regresia raportată („pozele ies din pagina A4") venea din faptul că paginile atașamentelor
/// intrau în dosar cu dimensiunea lor originală: frontendul compunea PDF-uri cu pagini de
/// ~580mm, iar scanurile vin în orice format. Assertul ieftin care o prinde e chiar dimensiunea
/// fiecărei pagini din rezultat — nu e nevoie de comparație vizuală.
/// </summary>
public sealed class DossierAssemblerTests
{
    // Toleranță de un punct: PdfSharp rotunjește dimensiunile la scriere.
    private const double TolerancePt = 1.0;
    private const double A4WidthPt = 595.28;
    private const double A4HeightPt = 841.89;

    public DossierAssemblerTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    [Fact]
    public void Assemble_NormalizesOversizedPagesToA4()
    {
        // Exact ce producea `imagesToPdf.ts` înainte de fix: o pagină cât patru A4-uri.
        byte[] oversized = PdfWithPages(widthPt: 1650, heightPt: 1237, pages: 1);

        byte[] dossier = DossierAssembler.Assemble(
            Cover(),
            [new DossierAttachment("Talon", "application/pdf", oversized)]);

        AllPagesShouldBeA4(dossier);
    }

    [Fact]
    public void Assemble_NormalizesForeignScansAndKeepsEveryPage()
    {
        // Un scan Letter de 3 pagini: copertă + separator + 3 pagini normalizate.
        byte[] letterScan = PdfWithPages(widthPt: 612, heightPt: 792, pages: 3);

        byte[] dossier = DossierAssembler.Assemble(
            Cover(),
            [new DossierAttachment("Cazier", "application/pdf", letterScan)]);

        PagesOf(dossier).Count.ShouldBe(5);
        AllPagesShouldBeA4(dossier);
    }

    [Fact]
    public void Assemble_KeepsLandscapeSourcesLandscape()
    {
        // O pagină lată nu se micșorează până la ilizibil: intră tot pe A4, dar rotit.
        byte[] wide = PdfWithPages(widthPt: 1000, heightPt: 500, pages: 1);

        byte[] dossier = DossierAssembler.Assemble(
            Cover(),
            [new DossierAttachment("Contract", "application/pdf", wide)]);

        (double Width, double Height) attachmentPage = PagesOf(dossier)[2];
        attachmentPage.Width.ShouldBeGreaterThan(attachmentPage.Height);
        AllPagesShouldBeA4(dossier);
    }

    [Fact]
    public void Assemble_ReplacesUnreadableAttachmentsInsteadOfFailing()
    {
        byte[] corrupt = [0x25, 0x50, 0x44, 0x46, 0x00, 0x01, 0x02, 0x03];

        byte[] dossier = DossierAssembler.Assemble(
            Cover(),
            [new DossierAttachment("Fișier corupt", "application/pdf", corrupt)]);

        // Copertă + pagina care spune că trebuie anexat manual. Dosarul nu crapă.
        PagesOf(dossier).Count.ShouldBe(2);
        AllPagesShouldBeA4(dossier);
    }

    [Fact]
    public void Assemble_WithoutAttachments_ReturnsTheCoverUnchanged()
    {
        byte[] cover = Cover();

        DossierAssembler.Assemble(cover, []).ShouldBe(cover);
    }

    private static void AllPagesShouldBeA4(byte[] pdf)
    {
        foreach ((double width, double height) in PagesOf(pdf))
        {
            // Orientarea poate diferi, dimensiunea nu.
            double shortSide = Math.Min(width, height);
            double longSide = Math.Max(width, height);

            shortSide.ShouldBe(A4WidthPt, TolerancePt);
            longSide.ShouldBe(A4HeightPt, TolerancePt);
        }
    }

    private static List<(double Width, double Height)> PagesOf(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using PdfDocument document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        return [.. Enumerable.Range(0, document.PageCount)
            .Select(i => (document.Pages[i].Width.Point, document.Pages[i].Height.Point))];
    }

    /// <summary>Coperta generată de QuestPDF, ca în fluxul real.</summary>
    private static byte[] Cover() =>
        Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.5f, Unit.Centimetre);
            page.Content().Text("Dosar de test");
        })).GeneratePdf();

    /// <summary>Un PDF cu pagini de dimensiunea cerută — sursele reale nu sunt A4.</summary>
    private static byte[] PdfWithPages(double widthPt, double heightPt, int pages)
    {
        using var document = new PdfDocument();

        for (int i = 0; i < pages; i++)
        {
            PdfPage page = document.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(widthPt);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(heightPt);
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
