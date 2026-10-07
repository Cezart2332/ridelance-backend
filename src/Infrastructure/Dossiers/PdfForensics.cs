using Application.Abstractions.Ai;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Content;

namespace Infrastructure.Dossiers;

/// <summary>
/// Metadatele unui PDF încărcat la înrolare, cu PdfPig: programul care l-a făcut, dacă are o
/// imagine înăuntru (scan, poză, ștampilă) și dacă e semnat electronic.
///
/// Un model care citește PDF-ul vede doar ce e pe pagină — o adeverință scrisă în Word și una
/// scanată arată la fel de „oficial” ca text. Fișierul însă spune cine l-a produs.
/// </summary>
internal sealed class PdfForensics(ILogger<PdfForensics> logger) : IDocumentForensics
{
    public PdfForensicsReport? Inspect(byte[] fileBytes, string contentType)
    {
        if (!contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var document = PdfDocument.Open(fileBytes);

            bool hasImages = document.GetPages().Any(HasImage);
            bool signed = document.TryGetForm(out AcroForm form) &&
                          form.Fields.Any(f => f.FieldType == AcroFieldType.Signature);

            return new PdfForensicsReport(
                document.Information.Producer,
                document.Information.Creator,
                hasImages,
                signed);
        }
#pragma warning disable CA1031 // Un PDF pe care PdfPig nu-l deschide nu spune nimic: rămâne doar citirea modelului.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogWarning(exception, "Metadatele PDF-ului nu au putut fi citite.");
            return null;
        }
    }

    private static bool HasImage(Page page) => page.GetImages().Any();
}
