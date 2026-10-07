using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Documents.GetByUser;

internal sealed class GetDocumentsByUserQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetDocumentsByUserQuery, List<DocumentSummary>>
{
    public async Task<Result<List<DocumentSummary>>> Handle(
        GetDocumentsByUserQuery query,
        CancellationToken cancellationToken)
    {
        List<DocumentSummary> documents = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == query.UserId)
            .OrderByDescending(d => d.UploadedAtUtc)
            .Select(d => new DocumentSummary(
                d.Id,
                d.OriginalFileName,
                d.ContentType,
                d.Category.ToString(),
                d.Status.ToString(),
                d.FileSize,
                d.UploadedAtUtc,
                d.ExpiresAtUtc,
                d.AiStatus.ToString(),
                d.AiSummary,
                d.AiDetectedType,
                d.AiExtractedExpiresAtUtc,
                d.AiRequiresManualReview,
                d.Origin.ToString(),
                d.Origin == DocumentOrigin.UserUpload,
                d.ReviewNote,
                // Ultima proiecție: EF o evaluează în proces, deci împărțirea pe rânduri e permisă.
                AiVerification.ReviewReasons.Split(d.AiSuspicionReasons)))
            .ToListAsync(cancellationToken);

        return documents;
    }
}
