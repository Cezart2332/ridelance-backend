using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Documents.AiVerification;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Documents.ExtractedFields;

/// <summary>O valoare confirmată/corectată de client pentru un câmp extras.</summary>
public sealed record ConfirmedFieldInput(string FieldKey, string? Value);

/// <summary>Clientul confirmă/corectează valorile precompletate din OCR pentru un document.</summary>
public sealed record ConfirmExtractedFieldsCommand(
    Guid UserId,
    Guid DocumentId,
    IReadOnlyList<ConfirmedFieldInput> Fields) : ICommand<ExtractedFieldsResponse>;

internal sealed class ConfirmExtractedFieldsCommandHandler(IApplicationDbContext context)
    : ICommandHandler<ConfirmExtractedFieldsCommand, ExtractedFieldsResponse>
{
    public async Task<Result<ExtractedFieldsResponse>> Handle(
        ConfirmExtractedFieldsCommand command,
        CancellationToken cancellationToken)
    {
        Document? document = await context.Documents
            .SingleOrDefaultAsync(d => d.Id == command.DocumentId && d.UserId == command.UserId, cancellationToken);

        if (document is null)
        {
            return Result.Failure<ExtractedFieldsResponse>(DocumentErrors.NotFound(command.DocumentId));
        }

        List<ExtractedField> rows = await context.ExtractedFields
            .Where(f => f.DocumentId == document.Id)
            .ToListAsync(cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;

        foreach (ConfirmedFieldInput input in command.Fields)
        {
            ExtractedField? row = rows.FirstOrDefault(f =>
                string.Equals(f.FieldKey, input.FieldKey, StringComparison.OrdinalIgnoreCase));

            if (row is null)
            {
                continue;
            }

            ExtractedFieldSpec? spec = DocumentAiCatalog.FieldSpec(document.Category, row.FieldKey);
            string? normalized = spec is null
                ? input.Value?.Trim()
                : ExtractedFieldValidators.Normalize(spec.Type, input.Value);

            row.ConfirmedValue = normalized;
            row.ConfirmedSource = ExtractedFieldSource.User;
            row.ConfirmedByUserId = command.UserId;
            row.ConfirmedAtUtc = nowUtc;
            row.ReviewState = ExtractedFieldReviewState.Confirmed;
            row.UpdatedAtUtc = nowUtc;

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                await ExtractedFieldApplier.ApplyAsync(context, document, row.FieldKey, normalized, cancellationToken);
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        var dtos = rows.OrderBy(f => f.FieldKey).Select(ExtractedFieldMapper.ToDto).ToList();

        return Result.Success(new ExtractedFieldsResponse(
            document.Id,
            document.Category.ToString(),
            document.AiConfidence,
            document.AiRequiresManualReview,
            dtos));
    }
}
