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

public sealed record DocumentAiAnalysisResult(
    bool MatchesExpectedType,
    bool IsReadable,
    bool IsValid,
    bool? IsExpired,
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
