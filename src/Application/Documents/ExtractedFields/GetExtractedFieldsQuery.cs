using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Documents.ExtractedFields;

/// <summary>Câmpurile extrase prin OCR pentru un document al userului curent (ecran de confirmare).</summary>
public sealed record GetExtractedFieldsQuery(Guid UserId, Guid DocumentId) : IQuery<ExtractedFieldsResponse>;

internal sealed class GetExtractedFieldsQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetExtractedFieldsQuery, ExtractedFieldsResponse>
{
    public async Task<Result<ExtractedFieldsResponse>> Handle(
        GetExtractedFieldsQuery query,
        CancellationToken cancellationToken)
    {
        Document? document = await context.Documents
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == query.DocumentId && d.UserId == query.UserId, cancellationToken);

        if (document is null)
        {
            return Result.Failure<ExtractedFieldsResponse>(DocumentErrors.NotFound(query.DocumentId));
        }

        List<ExtractedField> fields = await context.ExtractedFields
            .AsNoTracking()
            .Where(f => f.DocumentId == document.Id)
            .OrderBy(f => f.FieldKey)
            .ToListAsync(cancellationToken);

        var dtos = fields.Select(ExtractedFieldMapper.ToDto).ToList();

        return Result.Success(new ExtractedFieldsResponse(
            document.Id,
            document.Category.ToString(),
            document.AiConfidence,
            document.AiRequiresManualReview,
            dtos));
    }
}
