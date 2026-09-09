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
            [new DossierAttachment("Talon", "application/pdf", oversized)]);

        AllPagesShouldBeA4(dossier);
    }

    [Fact]
    public void Assemble_NormalizesForeignScansAndKeepsEveryPage()
    {
        // Un scan Letter de 3 pagini ⇒ exact 3 pagini în dosar.
        //
        // Erau 5 la început (separator per document), apoi 4 (opis + 3). Regula e „un document
        // sursă = exact numărul lui de pagini", iar acum nici opisul nu mai există.
        byte[] letterScan = PdfWithPages(widthPt: 612, heightPt: 792, pages: 3);

        byte[] dossier = DossierAssembler.Assemble(
            [new DossierAttachment("Cazier", "application/pdf", letterScan)]);

        PagesOf(dossier).Count.ShouldBe(3);
        AllPagesShouldBeA4(dossier);
    }

    [Fact]
    public void Assemble_TrimsBlankPagesLeftAtTheEndOfAScan()
    {
        // Două pagini scrise, două lăsate goale de scanner la final.
        byte[] scan = PdfWithPages(widthPt: 612, heightPt: 792, pages: 2, blankTrailingPages: 2);

        byte[] dossier = DossierAssembler.Assemble(
            [new DossierAttachment("Cazier", "application/pdf", scan)]);

        PagesOf(dossier).Count.ShouldBe(2);
    }

    [Fact]
    public void Assemble_KeepsBlankPagesInsideADocument()
    {
        // Versoul nescris al unui act rămâne în dosar: la ghișeu se numără paginile.
        using var document = new PdfDocument();
        AddPage(document, 612, 792, withContent: true);
        AddPage(document, 612, 792, withContent: false);
        AddPage(document, 612, 792, withContent: true);

        using var stream = new MemoryStream();
        document.Save(stream);

        byte[] dossier = DossierAssembler.Assemble(
            [new DossierAttachment("Contract", "application/pdf", stream.ToArray())]);

        PagesOf(dossier).Count.ShouldBe(3);
    }

    [Fact]
    public void Assemble_KeepsLandscapeSourcesLandscape()
    {
        // O pagină lată nu se micșorează până la ilizibil: intră tot pe A4, dar rotit.
        byte[] wide = PdfWithPages(widthPt: 1000, heightPt: 500, pages: 1);

        byte[] dossier = DossierAssembler.Assemble(
            [new DossierAttachment("Contract", "application/pdf", wide)]);

        // Prima pagină E documentul: dosarul nu mai are copertă.
        (double Width, double Height) attachmentPage = PagesOf(dossier)[0];
        attachmentPage.Width.ShouldBeGreaterThan(attachmentPage.Height);
        AllPagesShouldBeA4(dossier);
    }

    [Fact]
    public void Assemble_ReplacesUnreadableAttachmentsInsteadOfFailing()
    {
        byte[] corrupt = [0x25, 0x50, 0x44, 0x46, 0x00, 0x01, 0x02, 0x03];

        byte[] dossier = DossierAssembler.Assemble(
            [new DossierAttachment("Fișier corupt", "application/pdf", corrupt)]);

        // Pagina care spune că trebuie anexat manual, în locul documentului. Dosarul nu crapă.
        PagesOf(dossier).Count.ShouldBe(1);
        AllPagesShouldBeA4(dossier);
    }

    [Fact]
    public void Assemble_WithoutAttachments_SaysSoInsteadOfProducingAnEmptyFile()
    {
        // PdfSharp nu poate salva un document fără pagini, iar un fișier gol descărcat de client
        // e mai rău decât o filă care spune de ce e gol.
        byte[] dossier = DossierAssembler.Assemble([]);

        PagesOf(dossier).Count.ShouldBe(1);
    }

    /// <summary>
    /// Filigranul „TEST" nu are voie să depindă de fonturile mașinii.
    ///
    /// Regresia, raportată ca 500 la generarea dosarului: filigranul se desena cu
    /// <c>new XFont("Helvetica", …)</c>, iar PdfSharp cere pentru asta un font instalat. În
    /// containerul de producție nu există niciunul sub numele ăsta, deci ORICE dosar dintr-o
    /// sesiune de test pica cu „No appropriate font found" — nu doar filigranul, tot dosarul.
    /// </summary>
    [Fact]
    public void Assemble_StampsTheTestWatermarkWithoutASystemFont()
    {
        byte[] scan = PdfWithPages(widthPt: 612, heightPt: 792, pages: 2);

        byte[] dossier = DossierAssembler.Assemble(
            [new DossierAttachment("Cazier", "application/pdf", scan)],
            watermarkAsTest: true);

        PagesOf(dossier).Count.ShouldBe(2);
        AllPagesShouldBeA4(dossier);
    }

    /// <summary>
    /// Și ajunge pe FIECARE pagină: un dosar de test nu are voie să treacă drept unul depozabil,
    /// indiferent pe ce filă se uită cineva. Se numără formele desenate pe pagină — cu filigran e
    /// exact una în plus față de același dosar fără el.
    /// </summary>
    [Fact]
    public void Assemble_StampsEveryPageOfATestDossier()
    {
        byte[] scan = PdfWithPages(widthPt: 612, heightPt: 792, pages: 3);
        DossierAttachment[] attachments = [new("Cazier", "application/pdf", scan)];

        List<int> plain = XObjectCountsOf(DossierAssembler.Assemble(attachments));
        List<int> stamped = XObjectCountsOf(
            DossierAssembler.Assemble(attachments, watermarkAsTest: true));

        stamped.Count.ShouldBe(plain.Count);
        stamped.ShouldBe([.. plain.Select(count => count + 1)]);
    }

    /// <summary>Câte forme (XObject) sunt referite pe fiecare pagină.</summary>
    private static List<int> XObjectCountsOf(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using PdfDocument document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        return [.. Enumerable.Range(0, document.PageCount).Select(i =>
            document.Pages[i].Resources.Elements.GetDictionary("/XObject")?.Elements.Count ?? 0)];
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

    /// <summary>
    /// Un PDF cu pagini de dimensiunea cerută — sursele reale nu sunt A4.
    ///
    /// Paginile de conținut chiar au ceva desenat pe ele: un scan are conținut, iar assemblerul
    /// taie paginile goale rămase la final. Fără desen, fixture-ul ar descrie un document alb,
    /// nu un scan.
    /// </summary>
    private static byte[] PdfWithPages(double widthPt, double heightPt, int pages, int blankTrailingPages = 0)
    {
        using var document = new PdfDocument();

        for (int i = 0; i < pages; i++)
        {
            AddPage(document, widthPt, heightPt, withContent: true);
        }

        for (int i = 0; i < blankTrailingPages; i++)
        {
            AddPage(document, widthPt, heightPt, withContent: false);
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static void AddPage(PdfDocument document, double widthPt, double heightPt, bool withContent)
    {
        PdfPage page = document.AddPage();
        page.Width = PdfSharp.Drawing.XUnit.FromPoint(widthPt);
        page.Height = PdfSharp.Drawing.XUnit.FromPoint(heightPt);

        if (!withContent)
        {
            return;
        }

        using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        gfx.DrawLine(PdfSharp.Drawing.XPens.Black, 40, 40, 200, 200);
    }
}
