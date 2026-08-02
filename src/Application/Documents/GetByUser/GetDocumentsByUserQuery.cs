using Application.Abstractions.Messaging;
using Domain.Documents;

namespace Application.Documents.GetByUser;

public sealed record GetDocumentsByUserQuery(Guid UserId) : IQuery<List<DocumentSummary>>;

public sealed record DocumentSummary(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    string Category,
    string Status,
    long FileSize,
    DateTime UploadedAtUtc,
    DateTime? ExpiresAtUtc,
    string AiStatus,
    string? AiSummary,
    string? AiDetectedType,
    DateTime? AiExtractedExpiresAtUtc,
    /// <summary>
    /// Un câmp citit prin OCR n-a trecut validatorul determinist (ex. CAEN ≠ 4939) sau are
    /// încredere prea mică. Documentul rămâne acceptat, dar intră în coada adminului.
    /// </summary>
    bool AiRequiresManualReview);
