namespace Application.Abstractions.Ai;

/// <summary>
/// Ce spune fișierul PDF despre el însuși, fără model: cu ce program a fost făcut, dacă are o
/// imagine scanată înăuntru și dacă poartă o semnătură electronică.
/// </summary>
/// <param name="Producer">Câmpul <c>Producer</c> din metadatele PDF-ului.</param>
/// <param name="Creator">Câmpul <c>Creator</c> — de obicei aplicația în care s-a scris documentul.</param>
/// <param name="HasImages">Măcar o pagină conține o imagine: scan, poză sau ștampilă lipită.</param>
/// <param name="HasDigitalSignature">PDF-ul are un câmp de semnătură electronică.</param>
public sealed record PdfForensicsReport(
    string? Producer,
    string? Creator,
    bool HasImages,
    bool HasDigitalSignature)
{
    /// <summary>
    /// Programele de birou cu care se scrie un document de la zero. O instituție care emite acte
    /// în serie are aplicația ei; un PDF exportat direct din Word, fără scan și fără semnătură, e
    /// exact forma unei adeverințe „făcute acasă”.
    /// </summary>
    private static readonly string[] TextEditors =
    [
        "microsoft word", "microsoft® word", "word for", "libreoffice", "openoffice",
        "google docs", "canva", "wps office", "office 365", "microsoft: print to pdf",
    ];

    /// <summary>Programul care a făcut PDF-ul e un editor de text, nu o aplicație de emitere.</summary>
    public bool FromTextEditor =>
        IsEditor(Producer) || IsEditor(Creator);

    /// <summary>Numele programului, pentru motivul arătat adminului.</summary>
    public string? Tool => FirstEditor(Creator) ?? FirstEditor(Producer) ?? Creator ?? Producer;

    private static bool IsEditor(string? value) => FirstEditor(value) is not null;

    private static string? FirstEditor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TextEditors.Any(e => value.Contains(e, StringComparison.OrdinalIgnoreCase)) ? value.Trim() : null;
    }
}

/// <summary>Citește metadatele unui PDF încărcat. Null pentru orice nu e un PDF lizibil.</summary>
public interface IDocumentForensics
{
    PdfForensicsReport? Inspect(byte[] fileBytes, string contentType);
}
