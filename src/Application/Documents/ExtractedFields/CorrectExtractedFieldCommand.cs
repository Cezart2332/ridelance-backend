using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Documents.AiVerification;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Documents.ExtractedFields;

/// <summary>Adminul corectează o valoare extrasă; motivul modificării este obligatoriu (audit).</summary>
public sealed record CorrectExtractedFieldCommand(
    Guid AdminUserId,
    Guid FieldId,
    string? Value,
    string ChangeReason) : ICommand;

internal sealed class CorrectExtractedFieldCommandHandler(
    IApplicationDbContext context,
    IExtractedFieldApplier fieldApplier)
    : ICommandHandler<CorrectExtractedFieldCommand>
{
    private static readonly Error FieldNotFound = Error.NotFound(
        "ExtractedFields.NotFound",
        "Câmpul extras nu a fost găsit.");

    private static readonly Error ReasonRequired = Error.Problem(
        "ExtractedFields.ReasonRequired",
        "Motivul modificării este obligatoriu.");

    public async Task<Result> Handle(CorrectExtractedFieldCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ChangeReason))
        {
            return Result.Failure(ReasonRequired);
        }

        ExtractedField? row = await context.ExtractedFields
            .Include(f => f.Document)
            .SingleOrDefaultAsync(f => f.Id == command.FieldId, cancellationToken);

        if (row is null)
        {
            return Result.Failure(FieldNotFound);
        }

        Document document = row.Document;
        ExtractedFieldSpec? spec = DocumentAiCatalog.FieldSpec(document.Category, row.FieldKey);
        string? normalized = spec is null
            ? command.Value?.Trim()
            : ExtractedFieldValidators.Normalize(spec.Type, command.Value);

        DateTime nowUtc = DateTime.UtcNow;
        row.ConfirmedValue = normalized;
        row.ConfirmedSource = ExtractedFieldSource.Admin;
        row.ConfirmedByUserId = command.AdminUserId;
        row.ConfirmedAtUtc = nowUtc;
        row.ChangeReason = command.ChangeReason.Trim();
        row.ReviewState = ExtractedFieldReviewState.Confirmed;
        row.UpdatedAtUtc = nowUtc;

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            await fieldApplier.ApplyAsync(document, row.FieldKey, normalized, cancellationToken);
        }

        // Audit în jurnalul dosarului (dacă documentul e legat de un dosar PFA).
        if (document.PfaRegistrationId is Guid regId)
        {
            context.PfaActivityLogs.Add(new PfaActivityLog
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = regId,
                ActivityType = "ExtractedFieldCorrected",
                Description = $"Câmp „{row.FieldKey}” corectat manual. Motiv: {command.ChangeReason.Trim()}",
                PerformedByUserId = command.AdminUserId,
                CreatedAtUtc = nowUtc,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
