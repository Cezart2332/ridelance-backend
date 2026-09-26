namespace Application.Abstractions.Services;

public interface IPdfTextExtractor
{
    /// <summary>
    /// Textul din text layer-ul unui PDF, pagină cu pagină. <c>null</c> dacă fișierul nu e PDF, nu
    /// se poate citi sau nu are text (scanare) — atunci documentul se citește ca imagine.
    /// </summary>
    string? ExtractText(byte[] pdfBytes);
}
