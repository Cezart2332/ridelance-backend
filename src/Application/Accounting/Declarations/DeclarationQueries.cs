using System.Linq.Expressions;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Application.Accounting.Months;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Declarations;

/// <summary>Versiunile declarațiilor, în forma din contract.</summary>
internal static class DeclarationDtos
{
    public static async Task<List<DeclarationVersionDto>> VersionsAsync(
        IApplicationDbContext db,
        Expression<Func<DeclarationVersion, bool>> filter,
        CancellationToken cancellationToken)
    {
        var rows = await db.DeclarationVersions
            .AsNoTracking()
            .Where(filter)
            .Select(v => new
            {
                Version = v,
                SchemaVersion = v.Schema == null ? null : v.Schema.Version,
                Receipt = v.ReceiptDocument,
                RowVersion = EF.Property<uint>(v, "xmin"),
            })
            .ToListAsync(cancellationToken);

        var histories = rows.ToDictionary(
            row => row.Version.Id,
            row => AccountingJson.Deserialize<List<StatusHistoryRecord>>(row.Version.StatusHistoryJson, []));
        Dictionary<Guid, UserRef> users = await PlatformDocumentSupport.UsersAsync(
            db, histories.Values.SelectMany(history => history.Select(entry => entry.ByUserId)), cancellationToken);

        return [.. rows
            .OrderBy(row => row.Version.VersionNo)
            .Select(row => new DeclarationVersionDto(
                row.Version.Id,
                row.Version.DeclarationId,
                row.Version.VersionNo,
                row.Version.Kind,
                row.Version.Status,
                row.Version.Amount,
                row.SchemaVersion,
                row.Version.RectificationReason,
                row.Version.XmlDocumentId is not null,
                row.Version.PdfDocumentId is not null,
                row.Version.ReceiptNumber,
                row.Receipt is null ? null : new StoredFileRef(row.Receipt.Id, row.Receipt.OriginalFileName, row.Receipt.ContentType, row.Receipt.FileSize, string.Empty),
                [.. histories[row.Version.Id].Select(entry => new StatusHistoryEntry(
                    entry.From,
                    entry.To,
                    entry.At,
                    entry.ByUserId is { } by && users.TryGetValue(by, out UserRef? user) ? user : new UserRef(Guid.Empty, "Sistem"),
                    entry.Note))],
                row.Version.CreatedAtUtc,
                row.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)))];
    }

    public static async Task<DeclarationVersionDto?> VersionAsync(IApplicationDbContext db, Guid versionId, CancellationToken cancellationToken) =>
        (await VersionsAsync(db, v => v.Id == versionId, cancellationToken)).SingleOrDefault();
}

/// <summary><c>GET /accounting/declarations/{id}</c> — declarația cu toate versiunile.</summary>
public sealed record GetDeclarationQuery(Guid DeclarationId) : IQuery<DeclarationDetail>;

internal sealed class GetDeclarationQueryHandler(IApplicationDbContext db) : IQueryHandler<GetDeclarationQuery, DeclarationDetail>
{
    public async Task<Result<DeclarationDetail>> Handle(GetDeclarationQuery query, CancellationToken cancellationToken)
    {
        Declaration? declaration = await db.Declarations.AsNoTracking().SingleOrDefaultAsync(d => d.Id == query.DeclarationId, cancellationToken);
        if (declaration is null)
        {
            return Result.Failure<DeclarationDetail>(DeclarationErrors.DeclarationNotFound);
        }

        List<DeclarationVersionDto> versions = await DeclarationDtos.VersionsAsync(db, v => v.DeclarationId == declaration.Id, cancellationToken);
        return new DeclarationDetail(declaration.Id, declaration.PfaRegistrationId, declaration.Period, declaration.Type, versions, versions[^1].Id);
    }
}

/// <summary><c>GET /accounting/declaration-versions/{id}/breakdown</c> — „De unde vine suma”.</summary>
public sealed record GetDeclarationBreakdownQuery(Guid VersionId) : IQuery<DeclarationBreakdown>;

internal sealed class GetDeclarationBreakdownQueryHandler(IApplicationDbContext db) : IQueryHandler<GetDeclarationBreakdownQuery, DeclarationBreakdown>
{
    public async Task<Result<DeclarationBreakdown>> Handle(GetDeclarationBreakdownQuery query, CancellationToken cancellationToken)
    {
        DeclarationVersion? version = await db.DeclarationVersions
            .AsNoTracking()
            .Include(v => v.Lines)
            .ThenInclude(line => line.SourceDocument)
            .ThenInclude(document => document.SourceDocument)
            .SingleOrDefaultAsync(v => v.Id == query.VersionId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<DeclarationBreakdown>(DeclarationErrors.VersionNotFound);
        }

        var snapshot = DeclarationSnapshot.Read(version.SnapshotJson);
        var labels = (snapshot?.Calculation.Lines ?? [])
            .GroupBy(line => (line.SourceDocumentId, line.RuleCode))
            .ToDictionary(group => group.Key, group => group.First().SourceDocumentLabel);

        return new DeclarationBreakdown(
            [.. version.Lines
                .OrderBy(line => line.SupplierName, StringComparer.Ordinal)
                .ThenBy(line => line.Base)
                .Select(line => new DeclarationLineDto(
                    line.Id,
                    line.SourceDocumentId,
                    labels.GetValueOrDefault((line.SourceDocumentId, line.RuleCode)) ?? line.SourceDocument.SourceDocument.OriginalFileName,
                    line.RuleCode,
                    line.Base,
                    line.Rate,
                    line.Value,
                    line.Currency,
                    line.ExchangeRate,
                    line.Explanation,
                    line.SupplierName,
                    line.SupplierCountry,
                    line.SupplierVatId,
                    line.OperationType,
                    line.Treaty,
                    line.ResidenceCertValidFrom,
                    line.ResidenceCertValidTo))],
            version.Amount,
            snapshot?.Calculation.Explanation ?? string.Empty,
            snapshot?.Calculation.ExcludedRideIncome);
    }
}

/// <summary><c>GET /accounting/declaration-versions/{id}/validation</c> — <c>null</c> dacă nu a fost validată.</summary>
public sealed record GetDeclarationValidationQuery(Guid VersionId) : IQuery<ValidationResult?>;

internal sealed class GetDeclarationValidationQueryHandler(IApplicationDbContext db) : IQueryHandler<GetDeclarationValidationQuery, ValidationResult?>
{
    public async Task<Result<ValidationResult?>> Handle(GetDeclarationValidationQuery query, CancellationToken cancellationToken)
    {
        var version = await db.DeclarationVersions
            .AsNoTracking()
            .Where(v => v.Id == query.VersionId)
            .Select(v => new { v.ValidationResultJson })
            .SingleOrDefaultAsync(cancellationToken);
        if (version is null)
        {
            return Result.Failure<ValidationResult?>(DeclarationErrors.VersionNotFound);
        }

        StoredValidation? stored = AccountingJson.Deserialize<StoredValidation?>(version.ValidationResultJson, null);
        // Explicit: conversia implicită din null ar da un eșec (Error.NullValue).
        return Result.Success<ValidationResult?>(stored is null ? null : new ValidationResult(stored.Levels, stored.ValidatedAt, stored.ValidatorVersion));
    }
}

public enum DeclarationFileKind
{
    Xml = 0,
    Pdf = 1,
}

public sealed record DeclarationFile(byte[] Content, string FileName, string ContentType);

/// <summary><c>GET /accounting/declaration-versions/{id}/xml | pdf</c></summary>
public sealed record GetDeclarationFileQuery(Guid VersionId, DeclarationFileKind Kind) : IQuery<DeclarationFile>;

internal sealed class GetDeclarationFileQueryHandler(IApplicationDbContext db, DeclarationFiles files) : IQueryHandler<GetDeclarationFileQuery, DeclarationFile>
{
    public async Task<Result<DeclarationFile>> Handle(GetDeclarationFileQuery query, CancellationToken cancellationToken)
    {
        var version = await db.DeclarationVersions
            .AsNoTracking()
            .Where(v => v.Id == query.VersionId)
            .Select(v => new { v.XmlDocumentId, v.PdfDocumentId })
            .SingleOrDefaultAsync(cancellationToken);
        if (version is null)
        {
            return Result.Failure<DeclarationFile>(DeclarationErrors.VersionNotFound);
        }

        bool xml = query.Kind == DeclarationFileKind.Xml;
        (byte[] Content, Domain.Documents.Document Document)? file = await files.ReadAsync(xml ? version.XmlDocumentId : version.PdfDocumentId, cancellationToken);
        if (file is not { } found)
        {
            return Result.Failure<DeclarationFile>(DeclarationErrors.FileMissing(xml ? "XML-ul" : "PDF-ul"));
        }

        return new DeclarationFile(found.Content, found.Document.OriginalFileName, found.Document.ContentType);
    }
}
