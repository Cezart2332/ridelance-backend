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
    DateTime UploadedAtUtc);
