using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Documents;

/// <summary><c>GET /accounting/pfas/{pfaId}/platform-documents?period=</c></summary>
public sealed record ListPlatformDocumentsQuery(Guid PfaId, string Period) : IQuery<IReadOnlyList<PlatformDocumentListItem>>;

internal sealed class ListPlatformDocumentsQueryHandler(IApplicationDbContext db)
    : IQueryHandler<ListPlatformDocumentsQuery, IReadOnlyList<PlatformDocumentListItem>>
{
    public async Task<Result<IReadOnlyList<PlatformDocumentListItem>>> Handle(ListPlatformDocumentsQuery query, CancellationToken cancellationToken)
    {
        if (!PlatformDocumentSupport.IsValidPeriod(query.Period))
        {
            return Result.Failure<IReadOnlyList<PlatformDocumentListItem>>(AccountingErrors.InvalidPeriod);
        }

        List<PlatformDocument> documents = await db.PlatformDocuments
            .AsNoTracking()
            .Include(d => d.SourceDocument)
            .Include(d => d.Extractions.Where(e => e.IsCurrent))
            .Where(d => d.PfaRegistrationId == query.PfaId && d.Period == query.Period)
            .OrderBy(d => d.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, UserRef> users = await PlatformDocumentSupport.UsersAsync(db, documents.Select(d => (Guid?)d.UploadedByUserId), cancellationToken);

        var items = new List<PlatformDocumentListItem>();
        foreach (PlatformDocument document in documents)
        {
            DocumentExtraction? extraction = document.Extractions.FirstOrDefault();
            string? lockReason = await PlatformDocumentSupport.LockReasonAsync(db, document, cancellationToken);
            List<DocumentCheck> checks = AccountingJson.Deserialize<List<DocumentCheck>>(extraction?.ChecksResultJson, []);
            items.Add(new PlatformDocumentListItem
            {
                Id = document.Id,
                PfaId = document.PfaRegistrationId,
                Period = document.Period,
                Platform = document.Platform,
                DocumentType = document.DocumentType,
                FileName = document.SourceDocument.OriginalFileName,
                Status = PlatformDocumentSupport.EffectiveStatus(document, lockReason),
                UploadedBy = PlatformDocumentSupport.UserOrSystem(users, document.UploadedByUserId),
                UploadedAt = document.UploadedAtUtc,
                MainAmount = PlatformDocumentSupport.MainAmount(document.DocumentType, extraction),
                Currency = extraction?.Currency,
                FailedChecks = checks.Count(check => !check.Passed),
            });
        }

        return items;
    }
}

/// <summary><c>GET /accounting/platform-documents/{id}</c></summary>
public sealed record GetPlatformDocumentQuery(Guid Id) : IQuery<PlatformDocumentDetail>;

/// <summary>
/// Detaliul pentru ecranul de verificare. Verificările se refac la citire pe documentele
/// neconfirmate, fiindcă depind și de reguli (de ex. un furnizor adăugat între timp în registru);
/// statusul le urmează.
/// </summary>
internal sealed class GetPlatformDocumentQueryHandler(IApplicationDbContext db, IOptions<AccountingOptions> options)
    : IQueryHandler<GetPlatformDocumentQuery, PlatformDocumentDetail>
{
    public async Task<Result<PlatformDocumentDetail>> Handle(GetPlatformDocumentQuery query, CancellationToken cancellationToken)
    {
        PlatformDocument? document = await db.PlatformDocuments
            .Include(d => d.SourceDocument)
            .SingleOrDefaultAsync(d => d.Id == query.Id, cancellationToken);
        if (document is null)
        {
            return Result.Failure<PlatformDocumentDetail>(AccountingErrors.DocumentNotFound);
        }

        DocumentExtraction? extraction = await PlatformDocumentSupport.CurrentExtractionAsync(db, document.Id, cancellationToken);
        IReadOnlyList<DocumentCheck> checks = [];
        if (extraction is not null)
        {
            checks = await RefreshChecksAsync(document, extraction, cancellationToken);
        }

        return await PlatformDocumentDetails.BuildAsync(db, document, extraction, checks, cancellationToken);
    }

    private async Task<IReadOnlyList<DocumentCheck>> RefreshChecksAsync(PlatformDocument document, DocumentExtraction extraction, CancellationToken cancellationToken)
    {
        IReadOnlyList<DocumentCheck> checks = await PlatformDocumentSupport.RunChecksAsync(
            db, document, PlatformDocumentSupport.FieldsOf(extraction), options.Value, cancellationToken);

        if (document.Status is PlatformDocumentStatus.NeedsReview or PlatformDocumentStatus.PendingConfirmation)
        {
            string json = AccountingJson.Serialize(checks);
            PlatformDocumentStatus status = PlatformDocumentSupport.StatusAfter(checks);
            if (json != extraction.ChecksResultJson || status != document.Status)
            {
                extraction.ChecksResultJson = json;
                document.Status = status;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return checks;
    }
}

/// <summary><c>GET /accounting/platform-documents/{id}/file</c> — PDF-ul original, decriptat.</summary>
public sealed record GetPlatformDocumentFileQuery(Guid Id) : IQuery<PlatformDocumentFile>;

public sealed record PlatformDocumentFile(Stream Content, string ContentType, string FileName);

internal sealed class GetPlatformDocumentFileQueryHandler(IApplicationDbContext db, IFileEncryptionService encryption)
    : IQueryHandler<GetPlatformDocumentFileQuery, PlatformDocumentFile>
{
    public async Task<Result<PlatformDocumentFile>> Handle(GetPlatformDocumentFileQuery query, CancellationToken cancellationToken)
    {
        var file = await db.PlatformDocuments
            .AsNoTracking()
            .Where(d => d.Id == query.Id)
            .Select(d => new { d.SourceDocument.EncryptedFilePath, d.SourceDocument.EncryptionIv, d.SourceDocument.ContentType, d.SourceDocument.OriginalFileName })
            .SingleOrDefaultAsync(cancellationToken);
        if (file is null)
        {
            return Result.Failure<PlatformDocumentFile>(AccountingErrors.DocumentNotFound);
        }

        Stream content = await encryption.DecryptAndReadAsync(file.EncryptedFilePath, file.EncryptionIv, cancellationToken);
        return new PlatformDocumentFile(content, file.ContentType, file.OriginalFileName);
    }
}

/// <summary>Construcția <see cref="PlatformDocumentDetail"/>, comună citirii, editării și confirmării.</summary>
internal static class PlatformDocumentDetails
{
    public static async Task<PlatformDocumentDetail> BuildAsync(
        IApplicationDbContext db,
        PlatformDocument document,
        DocumentExtraction? extraction,
        IReadOnlyList<DocumentCheck> checks,
        CancellationToken cancellationToken)
    {
        string? lockReason = await PlatformDocumentSupport.LockReasonAsync(db, document, cancellationToken);
        Dictionary<Guid, UserRef> users = await PlatformDocumentSupport.UsersAsync(
            db,
            [document.UploadedByUserId, document.ReviewedByUserId, extraction?.CreatedByUserId],
            cancellationToken);

        return new PlatformDocumentDetail
        {
            Id = document.Id,
            PfaId = document.PfaRegistrationId,
            Period = document.Period,
            Platform = document.Platform,
            DocumentType = document.DocumentType,
            FileName = document.SourceDocument.OriginalFileName,
            Status = PlatformDocumentSupport.EffectiveStatus(document, lockReason),
            UploadedBy = PlatformDocumentSupport.UserOrSystem(users, document.UploadedByUserId),
            UploadedAt = document.UploadedAtUtc,
            MainAmount = PlatformDocumentSupport.MainAmount(document.DocumentType, extraction),
            Currency = extraction?.Currency,
            FailedChecks = checks.Count(check => !check.Passed),
            File = PlatformDocumentSupport.FileOf(document.SourceDocument, document.FileHash),
            Extraction = extraction is null ? null : ToDto(extraction, users),
            Checks = checks,
            IncludedIn = await PlatformDocumentSupport.IncludedInAsync(db, document.Id, cancellationToken),
            LockedReason = lockReason,
            ReviewedBy = document.ReviewedByUserId is null ? null : PlatformDocumentSupport.UserOrSystem(users, document.ReviewedByUserId),
            ReviewedAt = document.ReviewedAtUtc,
            ExtractionError = document.ExtractionError,
        };
    }

    private static DocumentExtractionDto ToDto(DocumentExtraction extraction, Dictionary<Guid, UserRef> users) =>
        new(
                    extraction.Version,
                    PlatformDocumentSupport.FieldsOf(extraction),
                    AccountingJson.Deserialize<Dictionary<string, string>>(extraction.SourceSnippetsJson, []),
                    extraction.ModelConfidence,
                    extraction.ModelId,
                    extraction.PromptVersion,
                    extraction.IsManualEdit,
                    AccountingJson.Deserialize<List<string>>(extraction.ManuallyEditedFieldsJson, []),
                    extraction.CreatedByUserId is null ? null : PlatformDocumentSupport.UserOrSystem(users, extraction.CreatedByUserId),
                    extraction.CreatedAtUtc);
}
