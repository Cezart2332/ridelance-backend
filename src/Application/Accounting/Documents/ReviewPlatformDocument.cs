using System.Globalization;
using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Documents;

/// <summary><c>PATCH /accounting/platform-documents/{id}/extraction</c> — câmpurile schimbate și motivul.</summary>
public sealed record UpdateExtractionCommand(Guid Id, JsonElement Fields, string Reason) : ICommand<PlatformDocumentDetail>;

/// <summary>
/// Editarea manuală (spec B1): o versiune nouă <c>IsManualEdit</c>, audit cu motiv obligatoriu,
/// verificările refăcute. Un document confirmat revine la verificare; unul blocat nu se editează.
/// </summary>
internal sealed class UpdateExtractionCommandHandler(IApplicationDbContext db, IUserContext userContext, IOptions<AccountingOptions> options)
    : ICommandHandler<UpdateExtractionCommand, PlatformDocumentDetail>
{
    public async Task<Result<PlatformDocumentDetail>> Handle(UpdateExtractionCommand command, CancellationToken cancellationToken)
    {
        string reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return Result.Failure<PlatformDocumentDetail>(AccountingErrors.ReasonRequired);
        }

        PlatformDocument? document = await db.PlatformDocuments.Include(d => d.SourceDocument).SingleOrDefaultAsync(d => d.Id == command.Id, cancellationToken);
        if (document is null)
        {
            return Result.Failure<PlatformDocumentDetail>(AccountingErrors.DocumentNotFound);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, document.PfaRegistrationId, document.Period, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<PlatformDocumentDetail>(writable.Error);
        }

        if (await PlatformDocumentSupport.LockReasonAsync(db, document, cancellationToken) is { } locked)
        {
            return Result.Failure<PlatformDocumentDetail>(AccountingErrors.DocumentLocked(locked));
        }

        DocumentExtraction? previous = await PlatformDocumentSupport.CurrentExtractionAsync(db, document.Id, cancellationToken);
        if (previous is null)
        {
            return Result.Failure<PlatformDocumentDetail>(AccountingErrors.NotExtracted);
        }

        ExtractedFields before = PlatformDocumentSupport.FieldsOf(previous);
        Result<ExtractedFields> merged = ExtractedFieldsPatch.Apply(before, command.Fields);
        if (merged.IsFailure)
        {
            return Result.Failure<PlatformDocumentDetail>(merged.Error);
        }

        ExtractedFields after = merged.Value;
        List<string> changed = ExtractedFieldsPatch.ChangedFields(before, after);
        if (changed.Count == 0)
        {
            return Result.Failure<PlatformDocumentDetail>(AccountingErrors.NoChanges);
        }

        IReadOnlyList<DocumentCheck> checks = await PlatformDocumentSupport.RunChecksAsync(db, document, after, options.Value, cancellationToken);
        Dictionary<string, string> snippets = AccountingJson.Deserialize<Dictionary<string, string>>(previous.SourceSnippetsJson, []);
        changed.ForEach(field => snippets.Remove(field));
        List<string> manual = AccountingJson.Deserialize<List<string>>(previous.ManuallyEditedFieldsJson, []);

        previous.IsCurrent = false;
        var extraction = new DocumentExtraction
        {
            Id = Guid.NewGuid(),
            PlatformDocumentId = document.Id,
            Version = previous.Version + 1,
            IsCurrent = true,
            SourceSnippetsJson = AccountingJson.Serialize(snippets),
            ChecksResultJson = AccountingJson.Serialize(checks),
            ModelConfidence = previous.ModelConfidence,
            ModelId = previous.ModelId,
            PromptVersion = previous.PromptVersion,
            IsManualEdit = true,
            ManuallyEditedFieldsJson = AccountingJson.Serialize(manual.Union(changed).ToList()),
            EditReason = reason,
            CreatedByUserId = userContext.UserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        PlatformDocumentSupport.Apply(extraction, after);
        db.DocumentExtractions.Add(extraction);

        document.Status = PlatformDocumentSupport.StatusAfter(checks);
        document.ReviewedByUserId = null;
        document.ReviewedAtUtc = null;

        AccountingAudit.Record(
            db, document.PfaRegistrationId, nameof(DocumentExtraction), document.Id, "MANUAL_EDIT",
            ExtractedFieldsPatch.Pick(before, changed), ExtractedFieldsPatch.Pick(after, changed), reason, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        await Months.PreCheck.RefreshIfProcessedAsync(db, document.PfaRegistrationId, document.Period, options.Value, cancellationToken);

        return await PlatformDocumentDetails.BuildAsync(db, document, extraction, checks, cancellationToken);
    }
}

/// <summary><c>POST /accounting/platform-documents/{id}/confirm</c></summary>
public sealed record ConfirmPlatformDocumentCommand(Guid Id) : ICommand<PlatformDocumentDetail>;

/// <summary>Confirmarea (spec B1): permisă doar când toate verificările trec.</summary>
internal sealed class ConfirmPlatformDocumentCommandHandler(IApplicationDbContext db, IUserContext userContext, IOptions<AccountingOptions> options)
    : ICommandHandler<ConfirmPlatformDocumentCommand, PlatformDocumentDetail>
{
    public async Task<Result<PlatformDocumentDetail>> Handle(ConfirmPlatformDocumentCommand command, CancellationToken cancellationToken)
    {
        PlatformDocument? document = await db.PlatformDocuments.Include(d => d.SourceDocument).SingleOrDefaultAsync(d => d.Id == command.Id, cancellationToken);
        if (document is null)
        {
            return Result.Failure<PlatformDocumentDetail>(AccountingErrors.DocumentNotFound);
        }

        Result<IReadOnlyList<DocumentCheck>> confirmed = await PlatformDocumentConfirmation.ConfirmAsync(db, document, userContext.UserId, options.Value, null, cancellationToken);
        if (confirmed.IsFailure)
        {
            return Result.Failure<PlatformDocumentDetail>(confirmed.Error);
        }

        await db.SaveChangesAsync(cancellationToken);
        await Months.PreCheck.RefreshIfProcessedAsync(db, document.PfaRegistrationId, document.Period, options.Value, cancellationToken);
        DocumentExtraction? extraction = await PlatformDocumentSupport.CurrentExtractionAsync(db, document.Id, cancellationToken);
        return await PlatformDocumentDetails.BuildAsync(db, document, extraction, confirmed.Value, cancellationToken);
    }
}

/// <summary><c>POST /accounting/platform-documents/confirm-bulk</c> — întoarce ce a sărit și de ce.</summary>
public sealed record ConfirmPlatformDocumentsBulkCommand(IReadOnlyList<Guid> Ids) : ICommand<ConfirmBulkResult>;

internal sealed class ConfirmPlatformDocumentsBulkCommandHandler(IApplicationDbContext db, IUserContext userContext, IOptions<AccountingOptions> options)
    : ICommandHandler<ConfirmPlatformDocumentsBulkCommand, ConfirmBulkResult>
{
    public async Task<Result<ConfirmBulkResult>> Handle(ConfirmPlatformDocumentsBulkCommand command, CancellationToken cancellationToken)
    {
        var confirmed = new List<Guid>();
        var skipped = new List<SkippedItem>();
        var touched = new HashSet<(Guid PfaId, string Period)>();

        foreach (Guid id in command.Ids.Distinct())
        {
            PlatformDocument? document = await db.PlatformDocuments.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (document is null)
            {
                skipped.Add(new SkippedItem(id, AccountingErrors.DocumentNotFound.Description));
                continue;
            }

            Result<IReadOnlyList<DocumentCheck>> result = await PlatformDocumentConfirmation.ConfirmAsync(
                db, document, userContext.UserId, options.Value, "Confirmare în bloc", cancellationToken);
            if (result.IsFailure)
            {
                skipped.Add(new SkippedItem(id, result.Error.Description));
                continue;
            }

            confirmed.Add(id);
            touched.Add((document.PfaRegistrationId, document.Period));
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach ((Guid pfaId, string period) in touched)
        {
            await Months.PreCheck.RefreshIfProcessedAsync(db, pfaId, period, options.Value, cancellationToken);
        }

        return new ConfirmBulkResult(confirmed, skipped);
    }
}

/// <summary>Regula comună confirmării simple și celei în bloc.</summary>
internal static class PlatformDocumentConfirmation
{
    public static async Task<Result<IReadOnlyList<DocumentCheck>>> ConfirmAsync(
        IApplicationDbContext db,
        PlatformDocument document,
        Guid userId,
        AccountingOptions options,
        string? reason,
        CancellationToken cancellationToken)
    {
        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, document.PfaRegistrationId, document.Period, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DocumentCheck>>(writable.Error);
        }

        DocumentExtraction? extraction = await PlatformDocumentSupport.CurrentExtractionAsync(db, document.Id, cancellationToken);
        if (extraction is null)
        {
            return Result.Failure<IReadOnlyList<DocumentCheck>>(AccountingErrors.NotExtracted);
        }

        IReadOnlyList<DocumentCheck> checks = await PlatformDocumentSupport.RunChecksAsync(
            db, document, PlatformDocumentSupport.FieldsOf(extraction), options, cancellationToken);
        extraction.ChecksResultJson = AccountingJson.Serialize(checks);

        if (document.Status is PlatformDocumentStatus.NeedsReview or PlatformDocumentStatus.PendingConfirmation)
        {
            document.Status = PlatformDocumentSupport.StatusAfter(checks);
        }

        if (document.Status == PlatformDocumentStatus.NeedsReview)
        {
            DocumentCheck failed = checks.First(check => !check.Passed);
            return Result.Failure<IReadOnlyList<DocumentCheck>>(
                reason is null ? AccountingErrors.ChecksFailed : Error.Conflict(AccountingErrors.ChecksFailed.Code, failed.Message));
        }

        if (document.Status != PlatformDocumentStatus.PendingConfirmation)
        {
            return Result.Failure<IReadOnlyList<DocumentCheck>>(AccountingErrors.NotPendingConfirmation(document.Status.ToString()));
        }

        document.Status = PlatformDocumentStatus.Confirmed;
        document.ReviewedByUserId = userId;
        document.ReviewedAtUtc = DateTime.UtcNow;
        AccountingAudit.Record(
            db, document.PfaRegistrationId, nameof(PlatformDocument), document.Id, "CONFIRM",
            new { status = PlatformDocumentStatus.PendingConfirmation }, new { status = PlatformDocumentStatus.Confirmed }, reason, userId);
        return Result.Success(checks);
    }
}

/// <summary>Aplicarea unui patch JSON peste câmpurile citite (numele din contract, camelCase).</summary>
internal static class ExtractedFieldsPatch
{
    public static Result<ExtractedFields> Apply(ExtractedFields fields, JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<ExtractedFields>(AccountingErrors.InvalidField("fields"));
        }

        ExtractedFields result = fields;
        foreach (JsonProperty property in patch.EnumerateObject())
        {
            JsonElement value = property.Value;
            bool isNull = value.ValueKind == JsonValueKind.Null;
            try
            {
                result = property.Name switch
                {
                    "supplierName" => result with { SupplierName = Text(value) },
                    "supplierCountry" => result with { SupplierCountry = Text(value)?.ToUpperInvariant() },
                    "supplierVatId" => result with { SupplierVatId = Text(value)?.ToUpperInvariant() },
                    "invoiceNumber" => result with { InvoiceNumber = Text(value) },
                    "invoiceDate" => result with { InvoiceDate = isNull ? null : Date(value) },
                    "periodFrom" => result with { PeriodFrom = isNull ? null : Date(value) },
                    "periodTo" => result with { PeriodTo = isNull ? null : Date(value) },
                    "currency" => result with { Currency = Text(value)?.ToUpperInvariant() },
                    "amount" => result with { Amount = isNull ? null : value.GetDecimal() },
                    "commissionAmount" => result with { CommissionAmount = isNull ? null : value.GetDecimal() },
                    "otherAmounts" => result with { OtherAmounts = value.Deserialize<List<OtherAmount>>(AccountingJson.Options) ?? [] },
                    _ => throw new FormatException(property.Name),
                };
            }
            catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
            {
                return Result.Failure<ExtractedFields>(AccountingErrors.InvalidField(property.Name));
            }
        }

        return result;
    }

    public static List<string> ChangedFields(ExtractedFields before, ExtractedFields after) =>
        [.. FieldValues(before).Where(pair => AccountingJson.Serialize(pair.Value) != AccountingJson.Serialize(FieldValues(after)[pair.Key])).Select(pair => pair.Key)];

    public static Dictionary<string, object?> Pick(ExtractedFields fields, IEnumerable<string> keys)
    {
        Dictionary<string, object?> values = FieldValues(fields);
        return keys.ToDictionary(key => key, key => values[key]);
    }

    private static Dictionary<string, object?> FieldValues(ExtractedFields fields) => new()
    {
        ["supplierName"] = fields.SupplierName,
        ["supplierCountry"] = fields.SupplierCountry,
        ["supplierVatId"] = fields.SupplierVatId,
        ["invoiceNumber"] = fields.InvoiceNumber,
        ["invoiceDate"] = fields.InvoiceDate,
        ["periodFrom"] = fields.PeriodFrom,
        ["periodTo"] = fields.PeriodTo,
        ["currency"] = fields.Currency,
        ["amount"] = fields.Amount,
        ["commissionAmount"] = fields.CommissionAmount,
        ["otherAmounts"] = fields.OtherAmounts,
    };

    private static string? Text(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string? text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static DateOnly Date(JsonElement value) =>
        DateOnly.ParseExact(value.GetString() ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
