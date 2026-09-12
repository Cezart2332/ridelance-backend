using Application.Abstractions.Dossiers;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
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
    /// Lipește documentele încărcate, în ordine, într-un singur PDF. Un atașament ilizibil NU pică
    /// dosarul — primește în loc o pagină care spune că trebuie anexat manual.
    ///
    /// Fără copertă: la ghișeu se depun actele, iar o filă cu antetul nostru și cu date pe care
    /// funcționarul le are deja în formular era o pagină de aruncat înaintea fiecărui dosar.
    /// </summary>
    public static byte[] Assemble(IReadOnlyList<DossierAttachment> attachments)
    {
        using var output = new PdfDocument();

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

        // PdfSharp nu poate salva un document fără pagini, iar un fișier gol e mai rău decât o
        // filă care spune de ce e gol.
        if (output.PageCount == 0)
        {
            AppendPdf(output, SkippedPage(
                "Dosar gol",
                "Nu există niciun document încărcat pentru dosarul acesta."));
        }

        using var stream = new MemoryStream();
        output.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Regula dosarului, din specul de fix-uri §9: <b>un document sursă = exact numărul lui de
    /// pagini</b>. Fără pagini separator, fără copertă per document, fără pagini albe.
    ///
    /// Singurele pagini care nu vin dintr-o sursă sunt opisul (una, la început, generat de
    /// <c>ArrDossierGenerator</c>) și pagina de „nu s-a putut atașa", care înlocuiește un
    /// document — deci nu se adaugă peste el.
    /// </summary>
    private static void AppendAttachment(PdfDocument output, DossierAttachment attachment)
    {
        if (ImageContentTypes.Contains(attachment.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            // O poză = o pagină. Eticheta stă în antetul aceleiași pagini, nu pe una separată.
            AppendPdf(output, ImagePage(attachment));
            return;
        }

        // Două fluxuri peste același conținut: PdfSharp consumă poziția, iar cele două API-uri
        // (citirea paginilor și forma desenabilă) n-au voie să și-o fure una alteia.
        using var inspected = new MemoryStream(attachment.Content);
        using PdfDocument reader = PdfReader.Open(inspected, PdfDocumentOpenMode.Import);

        using var source = new MemoryStream(attachment.Content);
        using var form = XPdfForm.FromStream(source);

        // Scanerele și convertoarele lasă frecvent pagini goale LA FINAL. Alea se taie — specul
        // cere exact asta. O pagină goală din INTERIORUL documentului rămâne: acolo poate fi
        // versoul nescris al unui act, iar la ghișeu se numără paginile.
        //
        // Dacă tot documentul e gol, păstrăm prima pagină: mai bine o filă albă în dosar decât
        // un document care dispare fără urmă.
        int lastPage = form.PageCount;
        while (lastPage > 1 && IsBlank(reader.Pages[lastPage - 1]))
        {
            lastPage--;
        }

        for (int i = 1; i <= lastPage; i++)
        {
            // O pagină care nu e decât o poză se reface din poză: așa ajunge și ea prin curățare,
            // nu doar pozele încărcate direct.
            byte[]? photo = SoloImage(reader.Pages[i - 1]);
            if (photo is not null)
            {
                AppendPdf(output, PhotoPage(photo, attachment.RotationDegrees));
                continue;
            }

            form.PageNumber = i;
            AppendNormalizedToA4(output, form);
        }
    }

    /// <summary>
    /// Pagina n-are nimic de desenat: fluxul de conținut e gol (sau doar spații) și nu există
    /// resurse XObject de plasat.
    ///
    /// Conservator intenționat: la orice îndoială (flux necitibil, resurse prezente) răspunde
    /// „nu e goală". O pagină în plus e o problemă de cosmetică; una lipsă din dosar e o
    /// respingere la ghișeu.
    /// </summary>
    private static bool IsBlank(PdfPage page)
    {
        try
        {
            if (page.Resources.Elements.ContainsKey("/XObject"))
            {
                return false;
            }

            PdfContent content = page.Contents.CreateSingleContent();
            byte[] stream = content.Stream?.UnfilteredValue ?? [];

            return stream.All(b => b is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or 0);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
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
    /// Singurul loc din care se randează o imagine în dosar.
    ///
    /// Două lucruri pe care le face acum și nu le făcea:
    ///
    /// Pagina ia orientarea pozei. Un act fotografiat pe lat intra pe o pagină portret, într-o
    /// casetă de 240mm înălțime, și ieșea o poză mică în jumătatea de sus a unei file goale.
    ///
    /// Și respectă EXIF-ul. Telefoanele scriu pixelii în portret și lasă în EXIF indicația
    /// „rotește la afișare"; PDF-ul n-are așa ceva, deci actul ajungea culcat. Rotația se aplică
    /// aici, o dată, la desenare.
    ///
    /// <c>FitArea</c> peste tot. <c>FitWidth</c>/<c>FitHeight</c> sunt interzise: fiecare din ele
    /// garantează depășirea pe cealaltă axă.
    /// </summary>
    private static byte[] ImagePage(DossierAttachment attachment) =>
        PhotoPage(attachment.Content, attachment.RotationDegrees, attachment.Label);

    /// <summary>
    /// O poză, curățată și pusă pe pagina ei.
    ///
    /// Rotația și decupajul se fac pe pixeli, în <see cref="DocumentImage"/>, nu pe container:
    /// aici trebuie știute laturile finale ca să se aleagă orientarea paginii, iar o rotire făcută
    /// la desenare le-ar lăsa pe cele vechi. Ce iese e actul fără fundal, drept.
    /// </summary>
    private static byte[] PhotoPage(byte[] content, int rotationDegrees, string? label = null)
    {
        var original = ImageInfo.Read(content);
        byte[] tidied = DocumentImage.Tidy(content, original.Orientation, rotationDegrees);
        var info = ImageInfo.Read(tidied);

        return Page(
            label,
            body => body.AlignCenter().AlignMiddle().Image(tidied).FitArea(),
            info.IsLandscape);
    }

    /// <summary>
    /// Poza dintr-o pagină de PDF care nu conține altceva — adică exact ce produce aplicația când
    /// combină fotografiile într-un PDF înainte de upload (<c>src/utils/imagesToPdf.ts</c>).
    ///
    /// Contează pentru că pe drumul ăsta vin mai toate pozele: fără extragere, actul rămâne
    /// îngropat într-o pagină pe care n-o putem decupa, cu tot fundalul lui.
    ///
    /// Null când pagina are text, mai multe imagini sau o codificare pe care n-o putem scoate
    /// întreagă — atunci pagina intră în dosar așa cum e.
    /// </summary>
    private static byte[]? SoloImage(PdfPage page)
    {
        try
        {
            PdfDictionary resources = page.Resources;
            if (resources.Elements.ContainsKey("/Font"))
            {
                return null;
            }

            PdfDictionary? xobjects = resources.Elements.GetDictionary("/XObject");
            if (xobjects is null || xobjects.Elements.Count != 1)
            {
                return null;
            }

            string key = xobjects.Elements.Keys.First();
            if (xobjects.Elements.GetObject(key) is not PdfDictionary image
                || image.Elements.GetName("/Subtype") != "/Image"
                || image.Stream is null)
            {
                return null;
            }

            // Doar JPEG: fluxul e chiar fișierul, deci se poate scoate fără să-l recompunem.
            string filter = image.Elements["/Filter"]?.ToString() ?? string.Empty;

            return filter.Contains("DCTDecode", StringComparison.Ordinal) ? image.Stream.Value : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static byte[] SkippedPage(string label, string reason) =>
        Page(label, content => content.Text(reason).FontColor(Colors.Red.Darken2));

    private static byte[] Page(string? label, Action<IContainer> body, bool landscape = false) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken4));

                // Fără etichetă pentru paginile scoase dintr-un PDF: acolo regula e „o pagină
                // sursă = o pagină", iar un antet ar fi text adăugat de noi peste actul depus.
                if (label is not null)
                {
                    page.Header().PaddingBottom(10).Text(label).SemiBold().FontSize(13);
                }

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
