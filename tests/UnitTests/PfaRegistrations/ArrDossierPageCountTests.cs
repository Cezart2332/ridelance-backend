using Application.Abstractions.Dossiers;
using Infrastructure.Dossiers;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Infrastructure;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// Regula dosarului ARR, din specul de fix-uri §9: <b>un document sursă = exact numărul lui de
/// pagini</b>. Nicio pagină separator, nicio copertă per document, nicio pagină albă.
///
/// Testul e cerut explicit de spec pentru că fix-ul a regresat: un separator adăugat înaintea
/// fiecărui PDF umfla dosarul cu o pagină per document, iar la ghișeu asta se vede.
/// </summary>
public sealed class ArrDossierPageCountTests
{
    /// <summary>Opisul: singura pagină din dosar care nu vine dintr-un document încărcat.</summary>
    private const int IndexPages = 1;

    static ArrDossierPageCountTests() => QuestPDF.Settings.License = LicenseType.Community;

    [Fact]
    public void GenerateArrDossier_HasExactlyOnePagePerSourcePage()
    {
        // 1 PDF de 2 pagini + 3 imagini = 5 pagini de conținut.
        DossierAttachment[] attachments =
        [
            new("Certificat de înregistrare", "application/pdf", PdfWithPages(2)),
            new("Aviz medical", "image/png", OnePixelPng()),
            new("Aviz psihologic", "image/png", OnePixelPng()),
            new("Cazier judiciar", "image/png", OnePixelPng()),
        ];

        byte[] dossier = new ArrDossierGenerator().GenerateArrDossier(DataWith(attachments));

        PageCountOf(dossier).ShouldBe(IndexPages + 5);
    }

    [Fact]
    public void GenerateArrDossier_DropsBlankTrailingPages()
    {
        // Trei pagini, ultima goală — cum iese din majoritatea scanerelor.
        DossierAttachment[] attachments =
        [
            new("Certificat constatator", "application/pdf", PdfWithPages(2, blankTrailingPages: 1)),
        ];

        byte[] dossier = new ArrDossierGenerator().GenerateArrDossier(DataWith(attachments));

        PageCountOf(dossier).ShouldBe(IndexPages + 2);
    }

    [Fact]
    public void GenerateArrDossier_WithoutAttachments_IsIndexOnly()
    {
        byte[] dossier = new ArrDossierGenerator().GenerateArrDossier(DataWith([]));

        PageCountOf(dossier).ShouldBe(IndexPages);
    }

    private static ArrDossierData DataWith(IReadOnlyList<DossierAttachment> attachments) =>
        new(
            "Popescu Ion",
            "12345678",
            "Popescu Ion PFA",
            "Str. Exemplu 1, Cluj-Napoca, Cluj",
            "ARR Cluj",
            30000,
            attachments,
            new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));

    private static int PageCountOf(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using PdfDocument document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    /// <summary>
    /// Un PDF cu <paramref name="contentPages"/> pagini pe care s-a desenat ceva, urmate de
    /// <paramref name="blankTrailingPages"/> pagini pe care nu s-a desenat nimic.
    /// </summary>
    private static byte[] PdfWithPages(int contentPages, int blankTrailingPages = 0)
    {
        using var document = new PdfDocument();

        for (int i = 0; i < contentPages; i++)
        {
            PdfPage page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawLine(XPens.Black, 40, 40, 200, 200);
        }

        for (int i = 0; i < blankTrailingPages; i++)
        {
            document.AddPage();
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    /// <summary>PNG 1×1 valid, cel mai mic conținut de imagine pe care îl acceptă generatorul.</summary>
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
