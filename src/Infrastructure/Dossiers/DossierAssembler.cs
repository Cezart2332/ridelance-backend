using Application.Abstractions.Dossiers;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Dossiers;

/// <summary>
/// Lipește documentele încărcate de driver după coperta generată cu QuestPDF, ca dosarul să poată
/// fi depus la ARR ca atare — nu doar ca listă de bifat.
///
/// De ce două biblioteci: QuestPDF încorporează imagini, dar nu poate importa pagini dintr-un PDF
/// străin (<c>Document.Merge</c> merge doar documente produse tot de el). Iar majoritatea
/// uploadurilor SUNT PDF-uri: frontendul combină pozele într-un PDF înainte de upload
/// (<c>src/utils/imagesToPdf.ts</c>). Deci imaginile trec prin QuestPDF, iar concatenarea finală
/// se face cu PdfSharp.
/// </summary>
internal static class DossierAssembler
{
    /// <summary>
    /// Plafon pentru totalul atașamentelor. Șase documente × 10 MB ar ajunge întregi în memorie
    /// și apoi într-un singur rând `Document`; peste prag, restul rămân doar în cuprins.
    /// </summary>
    private const long MaxAttachmentBytes = 40L * 1024 * 1024;

    private static readonly string[] ImageContentTypes = ["image/jpeg", "image/jpg", "image/png"];

    /// <summary>Marginea paginilor normalizate, în puncte (~15mm). Aceeași pe tot dosarul.</summary>
    private const double NormalizedMarginPt = 42;

    /// <summary>
    /// Înălțimea maximă a casetei de imagine, ca o poză portret să nu împingă conținutul peste
    /// pagină. Lățimea o dă containerul; înălțimea trebuie declarată explicit.
    /// </summary>
    private const float ImageBoxHeightMm = 240;

    /// <summary>
    /// Întoarce coperta cu atașamentele lipite după ea. Un atașament ilizibil NU pică dosarul —
    /// primește în loc o pagină care spune că trebuie anexat manual.
    /// </summary>
    public static byte[] Assemble(byte[] coverPdf, IReadOnlyList<DossierAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return coverPdf;
        }

        using var output = new PdfDocument();
        AppendPdf(output, coverPdf);

        long budget = MaxAttachmentBytes;

        foreach (DossierAttachment attachment in attachments)
        {
            if (attachment.Content.Length > budget)
            {
                AppendPdf(output, SkippedPage(
                    attachment.Label,
                    "Documentul depășește spațiul disponibil în dosar. Anexează-l separat."));
                continue;
            }

            budget -= attachment.Content.Length;

            try
            {
                AppendAttachment(output, attachment);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Un PDF parolat sau corupt nu are voie să blocheze generarea întregului dosar.
                AppendPdf(output, SkippedPage(
                    attachment.Label,
                    "Documentul nu a putut fi atașat automat. Anexează-l manual la dosar."));
            }
        }

        using var stream = new MemoryStream();
        output.Save(stream);
        return stream.ToArray();
    }

    private static void AppendAttachment(PdfDocument output, DossierAttachment attachment)
    {
        if (ImageContentTypes.Contains(attachment.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            AppendPdf(output, ImagePage(attachment));
            return;
        }

        // Deschidem sursa ÎNAINTE de separator: dacă PDF-ul e corupt, aruncă aici și nu rămâne
        // un separator orfan urmat de pagina de eroare.
        using var source = new MemoryStream(attachment.Content);
        using var form = XPdfForm.FromStream(source);
        int pageCount = form.PageCount;

        // Separator cu titlul cerinței, ca să se vadă unde începe fiecare document original.
        AppendPdf(output, SeparatorPage(attachment.Label));
        for (int i = 1; i <= pageCount; i++)
        {
            form.PageNumber = i;
            AppendNormalizedToA4(output, form);
        }
    }

    /// <summary>
    /// Redesenează o pagină străină pe o pagină A4 a dosarului, scalată să încapă și centrată.
    ///
    /// Fără asta, pagina intra în dosar cu dimensiunea ei originală — iar sursele reale nu sunt
    /// A4: scanurile vin în Letter sau la DPI-ul scannerului, iar PDF-urile compuse de aplicație
    /// din poze aveau, până la fixul din <c>src/utils/imagesToPdf.ts</c>, pagini de câteva sute de
    /// milimetri. Asta era cauza reală a „pozelor care ies din pagină", nu modul de încadrare al
    /// imaginii: QuestPDF folosea deja <c>FitArea</c>.
    ///
    /// Se desenează ca formă vectorială (<see cref="XPdfForm"/>), nu ca imagine rasterizată, ca
    /// textul documentelor scanate digital să rămână selectabil și fișierul să nu se umfle.
    /// </summary>
    private static void AppendNormalizedToA4(PdfDocument output, XPdfForm form)
    {
        PdfPage page = output.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        // O pagină lată intră tot pe A4, dar în landscape: altfel s-ar micșora până la ilizibil.
        page.Orientation = form.PointWidth > form.PointHeight
            ? PageOrientation.Landscape
            : PageOrientation.Portrait;

        using var gfx = XGraphics.FromPdfPage(page);

        double boxWidth = page.Width.Point - NormalizedMarginPt * 2;
        double boxHeight = page.Height.Point - NormalizedMarginPt * 2;
        double scale = Math.Min(boxWidth / form.PointWidth, boxHeight / form.PointHeight);

        double width = form.PointWidth * scale;
        double height = form.PointHeight * scale;

        gfx.DrawImage(
            form,
            NormalizedMarginPt + (boxWidth - width) / 2,
            NormalizedMarginPt + (boxHeight - height) / 2,
            width,
            height);
    }

    private static void AppendPdf(PdfDocument output, byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using PdfDocument source = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        for (int i = 0; i < source.PageCount; i++)
        {
            output.AddPage(source.Pages[i]);
        }
    }

    /// <summary>
    /// Singurul loc din care se randează o imagine în dosar. Caseta are aceeași lățime și aceeași
    /// înălțime maximă indiferent de poză: o landscape lasă spațiu sus/jos, una portret lasă
    /// spațiu stânga/dreapta, iar paginile arată uniform chiar dacă rapoartele diferă.
    ///
    /// <c>FitArea</c> peste tot. <c>FitWidth</c>/<c>FitHeight</c> sunt interzise: fiecare din ele
    /// garantează depășirea pe cealaltă axă.
    /// </summary>
    private static byte[] ImagePage(DossierAttachment attachment) =>
        Page(attachment.Label, content => content
            .Border(1).BorderColor(Colors.Grey.Lighten2)
            .Padding(4)
            .AlignCenter().AlignMiddle()
            .MaxHeight(ImageBoxHeightMm, Unit.Millimetre)
            .Image(attachment.Content)
            .FitArea());

    private static byte[] SeparatorPage(string label) =>
        Page(label, content => content.Text("Documentul original urmează pe paginile următoare.")
            .FontColor(Colors.Grey.Medium));

    private static byte[] SkippedPage(string label, string reason) =>
        Page(label, content => content.Text(reason).FontColor(Colors.Red.Darken2));

    private static byte[] Page(string label, Action<IContainer> body) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken4));

                page.Header().PaddingBottom(10).Text(label).SemiBold().FontSize(13);
                body(page.Content());
            });
        })
        // 150 DPI e pragul de la care un act scanat rămâne lizibil la print fără să umfle
        // dosarul. Fixat explicit, ca ieșirea să nu depindă de valorile implicite ale bibliotecii.
        .WithSettings(new DocumentSettings
        {
            ImageRasterDpi = 150,
            ImageCompressionQuality = ImageCompressionQuality.High,
        })
        .GeneratePdf();
}
