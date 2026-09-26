using System.Text;
using Application.Abstractions.Services;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Infrastructure.Accounting;

/// <summary>
/// Textul unui PDF cu PdfPig (spec contabilitate B1). Ordinea de citire a conținutului, nu
/// literele brute: sumele trebuie să rămână întregi („1.248,50”) ca <c>AMOUNT_IN_TEXT</c> să le
/// găsească.
/// </summary>
internal sealed class PdfPigTextExtractor(ILogger<PdfPigTextExtractor> logger) : IPdfTextExtractor
{
    public string? ExtractText(byte[] pdfBytes)
    {
        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            var text = new StringBuilder();
            foreach (Page page in document.GetPages())
            {
                text.AppendLine(ContentOrderTextExtractor.GetText(page));
            }

            string result = text.ToString();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
#pragma warning disable CA1031 // Orice PDF stricat înseamnă doar „fără text layer”: îl citește modelul.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogWarning(exception, "PDF-ul nu a putut fi citit cu PdfPig; se trimite ca imagine.");
            return null;
        }
    }
}
