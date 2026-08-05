using SharedKernel;

namespace Application.Abstractions.Ai;

/// <summary>Un câmp de business cerut modelului spre extragere (precompletare).</summary>
public sealed record AiFieldRequest(string Key, string Description, string Type, bool Required);

public sealed record DocumentAiAnalysisRequest(
    byte[] FileBytes,
    string ContentType,
    string FileName,
    string ExpectedDocumentLabel,
    string ExpectationDetails,
    bool ExpectsExpiryDate,
    IReadOnlyList<AiFieldRequest> Fields);

/// <summary>Valoarea extrasă pentru un câmp + încrederea auto-raportată de model (0..1).</summary>
public sealed record AiFieldResult(string Key, string? Value, double Confidence);

/// <summary>
/// Ce a citit modelul din document. <b>Doar extragere</b>: modelul nu decide dacă documentul e
/// valid și nu compară date cu prezentul.
///
/// Motivul e concret: modelul nu are un ceas. Îi injectam data curentă în prompt și îl puneam să
/// judece expirarea, iar el respingea acte perfect valabile pentru că „data eliberării e în
/// viitor". Comparațiile temporale se fac acum în C#, pe ceasul serverului
/// (vezi <c>DocumentDateValidator</c>).
/// </summary>
/// <param name="IssuedOn">
/// Data eliberării/emiterii, ISO 8601. Null când documentul nu o conține sau nu s-a putut citi.
/// </param>
/// <param name="ExpiresAt">
/// Data expirării/valabilității, ISO 8601. Null când documentul nu o conține.
/// </param>
public sealed record DocumentAiAnalysisResult(
    bool MatchesExpectedType,
    bool IsReadable,
    DateOnly? IssuedOn,
    DateOnly? ExpiresAt,
    string DetectedType,
    string Reason,
    IReadOnlyList<AiFieldResult> Fields,
    double OverallConfidence);

public interface IDocumentAiAnalyzer
{
    Task<Result<DocumentAiAnalysisResult>> AnalyzeAsync(
        DocumentAiAnalysisRequest request,
        CancellationToken cancellationToken);
}
