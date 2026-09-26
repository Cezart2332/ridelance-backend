using Application.Accounting.Contracts;
using Domain.Accounting;
using SharedKernel;

namespace Application.Abstractions.Ai;

/// <summary>Un document Uber/Bolt de citit (spec contabilitate B1).</summary>
/// <param name="PdfText">Textul din text layer; <c>null</c> dacă PDF-ul e doar imagine.</param>
public sealed record DocumentExtractionRequest(
    byte[] FileBytes,
    string ContentType,
    string FileName,
    string? PdfText,
    string Period);

/// <summary>
/// Ce a citit modelul: clasificarea, câmpurile tipate și, pentru fiecare valoare, textul exact din
/// document (<c>SourceSnippets</c>), ca ecranul de verificare să-l poată evidenția.
/// </summary>
public sealed record DocumentExtractionResult(
    PlatformDocumentType DocumentType,
    Platform? Platform,
    ExtractedFields Fields,
    IReadOnlyDictionary<string, string> SourceSnippets,
    double? Confidence,
    string ModelId,
    string PromptVersion);

/// <summary>
/// Citirea documentelor de platformă. <b>AI-ul doar citește</b> (spec §0 pct. 5): nicio decizie
/// fiscală, clasificare de deductibilitate sau calcul; verificările și statusul le decide codul.
/// </summary>
public interface IDocumentExtractor
{
    Task<Result<DocumentExtractionResult>> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken);
}
