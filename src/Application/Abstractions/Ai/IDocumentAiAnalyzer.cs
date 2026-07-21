using SharedKernel;

namespace Application.Abstractions.Ai;

public sealed record DocumentAiAnalysisRequest(
    byte[] FileBytes,
    string ContentType,
    string FileName,
    string ExpectedDocumentLabel,
    string ExpectationDetails,
    bool ExpectsExpiryDate);

public sealed record DocumentAiAnalysisResult(
    bool MatchesExpectedType,
    bool IsReadable,
    bool IsValid,
    bool? IsExpired,
    DateOnly? ExpiresAt,
    string DetectedType,
    string Reason);

public interface IDocumentAiAnalyzer
{
    Task<Result<DocumentAiAnalysisResult>> AnalyzeAsync(
        DocumentAiAnalysisRequest request,
        CancellationToken cancellationToken);
}
