using Application.Abstractions.Data;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Domain.Documents;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Documents;

/// <summary>Logica comună a documentelor de platformă: câmpuri, verificări, blocare, DTO-uri.</summary>
internal static class PlatformDocumentSupport
{
    /// <summary>Statusurile versiunii curente după care documentele sursă nu se mai pot edita (§3.1, Decizii pct. 3).</summary>
    private static readonly DeclarationStatus[] LockingStatuses =
    [
        DeclarationStatus.Validated,
        DeclarationStatus.ReadyToSign,
        DeclarationStatus.Signed,
        DeclarationStatus.Submitted,
        DeclarationStatus.Accepted,
    ];

    public static bool IsValidPeriod(string period) =>
        period.Length == 7 &&
        DateOnly.TryParseExact($"{period}-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

    public static ExtractedFields FieldsOf(DocumentExtraction extraction) => new(
        extraction.SupplierName,
        extraction.SupplierCountry,
        extraction.SupplierVatId,
        extraction.InvoiceNumber,
        extraction.InvoiceDate,
        extraction.PeriodFrom,
        extraction.PeriodTo,
        extraction.Currency,
        extraction.Amount,
        extraction.CommissionAmount,
        AccountingJson.Deserialize<List<OtherAmount>>(extraction.OtherAmountsJson, []));

    public static void Apply(DocumentExtraction extraction, ExtractedFields fields)
    {
        extraction.SupplierName = fields.SupplierName;
        extraction.SupplierCountry = fields.SupplierCountry;
        extraction.SupplierVatId = fields.SupplierVatId;
        extraction.InvoiceNumber = fields.InvoiceNumber;
        extraction.InvoiceDate = fields.InvoiceDate;
        extraction.PeriodFrom = fields.PeriodFrom;
        extraction.PeriodTo = fields.PeriodTo;
        extraction.Currency = fields.Currency;
        extraction.Amount = fields.Amount;
        extraction.CommissionAmount = fields.CommissionAmount;
        extraction.OtherAmountsJson = AccountingJson.Serialize(fields.OtherAmounts);
    }

    public static Task<DocumentExtraction?> CurrentExtractionAsync(IApplicationDbContext db, Guid documentId, CancellationToken cancellationToken) =>
        db.DocumentExtractions.SingleOrDefaultAsync(e => e.PlatformDocumentId == documentId && e.IsCurrent, cancellationToken);

    /// <summary>Rulează verificările deterministe, cu contextul citit din baza de date.</summary>
    public static async Task<IReadOnlyList<DocumentCheck>> RunChecksAsync(
        IApplicationDbContext db,
        PlatformDocument document,
        ExtractedFields fields,
        AccountingOptions options,
        CancellationToken cancellationToken)
    {
        List<SupplierTaxProfile> suppliers = await db.SupplierTaxProfiles.AsNoTracking().ToListAsync(cancellationToken);

        bool duplicate = fields.InvoiceNumber is not null && fields.SupplierVatId is not null &&
            await db.DocumentExtractions.AnyAsync(
                e => e.IsCurrent &&
                     e.PlatformDocumentId != document.Id &&
                     e.PlatformDocument.DocumentType == PlatformDocumentType.CommissionInvoice &&
                     e.SupplierVatId == fields.SupplierVatId &&
                     e.InvoiceNumber == fields.InvoiceNumber,
                cancellationToken);

        string? declaredElsewhere = await db.DeclarationLines
            .Where(line => line.SourceDocumentId == document.Id &&
                           line.DeclarationVersion.Status == DeclarationStatus.Accepted &&
                           line.DeclarationVersion.Declaration.Period != document.Period)
            .Select(line => line.DeclarationVersion.Declaration.Type + " pentru " + line.DeclarationVersion.Declaration.Period)
            .FirstOrDefaultAsync(cancellationToken);

        decimal? reportIncome = document.DocumentType == PlatformDocumentType.CommissionInvoice && document.Platform is not null
            ? await db.DocumentExtractions
                .Where(e => e.IsCurrent &&
                            e.PlatformDocument.PfaRegistrationId == document.PfaRegistrationId &&
                            e.PlatformDocument.Period == document.Period &&
                            e.PlatformDocument.Platform == document.Platform &&
                            e.PlatformDocument.DocumentType == PlatformDocumentType.PlatformReport)
                .Select(e => e.Amount)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return DocumentChecker.Run(
            new CheckSubject(document.Period, document.DocumentType, document.PdfText),
            fields,
            new CheckContext(suppliers, duplicate, declaredElsewhere, reportIncome, options));
    }

    /// <summary>Statusul după verificări, pentru un document încă neconfirmat.</summary>
    public static PlatformDocumentStatus StatusAfter(IEnumerable<DocumentCheck> checks) =>
        DocumentChecker.AllPassed(checks) ? PlatformDocumentStatus.PendingConfirmation : PlatformDocumentStatus.NeedsReview;

    /// <summary>
    /// De ce nu se mai poate edita un document confirmat; <c>null</c> dacă se poate. O rectificativă
    /// deschisă care îl include îl deblochează (Decizii pct. 3).
    /// </summary>
    public static async Task<string?> LockReasonAsync(IApplicationDbContext db, PlatformDocument document, CancellationToken cancellationToken)
    {
        if (document.Status is not (PlatformDocumentStatus.Confirmed or PlatformDocumentStatus.Locked))
        {
            return null;
        }

        bool periodClosed = await db.PfaAccountingPeriods.AnyAsync(
            p => p.PfaRegistrationId == document.PfaRegistrationId && p.Period == document.Period && p.Status == AccountingPeriodStatus.Closed,
            cancellationToken);
        if (periodClosed)
        {
            return $"Perioada {document.Period} e închisă.";
        }

        // Versiunile curente (cea mai mare pe declarație) care includ documentul.
        var current = await db.DeclarationLines
            .Where(line => line.SourceDocumentId == document.Id)
            .Select(line => line.DeclarationVersion)
            .Where(version => version.VersionNo == version.Declaration.Versions.Max(other => other.VersionNo))
            .Select(version => new { version.Kind, version.Status, version.VersionNo, version.Declaration.Type })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (current.Any(version => version.Kind == DeclarationVersionKind.Rectificative && version.Status == DeclarationStatus.Generated))
        {
            return null;
        }

        var locking = current.FirstOrDefault(version => LockingStatuses.Contains(version.Status));
        return locking is null
            ? null
            : $"Inclus în {locking.Type} v{locking.VersionNo}. Se deblochează doar printr-o rectificativă.";
    }

    public static async Task<IReadOnlyList<DeclarationReference>> IncludedInAsync(IApplicationDbContext db, Guid documentId, CancellationToken cancellationToken) =>
        await db.DeclarationLines
            .Where(line => line.SourceDocumentId == documentId)
            .Select(line => line.DeclarationVersion)
            .Distinct()
            .Select(version => new DeclarationReference(
                version.DeclarationId,
                version.Id,
                version.Declaration.Type,
                version.Declaration.Period,
                version.VersionNo,
                version.Kind,
                version.Status))
            .ToListAsync(cancellationToken);

    /// <summary>Scrierea e permisă doar pe un dosar activ și într-o perioadă deschisă.</summary>
    public static async Task<Result> EnsureWritableAsync(IApplicationDbContext db, Guid pfaId, string period, CancellationToken cancellationToken)
    {
        bool inactive = await db.PfaAccountingEngagements.AnyAsync(
            e => e.PfaRegistrationId == pfaId && e.Status == EngagementStatus.Inactive &&
                 !db.PfaAccountingEngagements.Any(other => other.PfaRegistrationId == pfaId && other.Status == EngagementStatus.Active),
            cancellationToken);
        if (inactive)
        {
            return Result.Failure(AccountingErrors.PfaReadOnly);
        }

        bool closed = await db.PfaAccountingPeriods.AnyAsync(
            p => p.PfaRegistrationId == pfaId && p.Period == period && p.Status == AccountingPeriodStatus.Closed,
            cancellationToken);
        return closed ? Result.Failure(AccountingErrors.PeriodClosed(period)) : Result.Success();
    }

    public static async Task<Dictionary<Guid, UserRef>> UsersAsync(IApplicationDbContext db, IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        List<User> users = await db.Users.AsNoTracking().Where(u => wanted.Contains(u.Id)).ToListAsync(cancellationToken);
        return users.ToDictionary(u => u.Id, u => new UserRef(u.Id, $"{u.FirstName} {u.LastName}".Trim()));
    }

    /// <summary>Numele unui utilizator; extracția automată nu are autor.</summary>
    public static UserRef UserOrSystem(Dictionary<Guid, UserRef> users, Guid? id) =>
        id is { } value && users.TryGetValue(value, out UserRef? user) ? user : new UserRef(Guid.Empty, "Citire automată");

    public static decimal? MainAmount(PlatformDocumentType type, DocumentExtraction? extraction)
    {
        if (extraction is null)
        {
            return null;
        }

        return type == PlatformDocumentType.PlatformReport ? extraction.Amount : extraction.CommissionAmount;
    }

    public static PlatformDocumentStatus EffectiveStatus(PlatformDocument document, string? lockReason) =>
        lockReason is null ? document.Status : PlatformDocumentStatus.Locked;

    public static StoredFileRef FileOf(Document file, string hash) =>
        new(file.Id, file.OriginalFileName, file.ContentType, file.FileSize, hash);

    public static DocumentCategory CategoryFor(Platform? platform, PlatformDocumentType type) => (platform, type) switch
    {
        (Platform.Bolt, PlatformDocumentType.CommissionInvoice) => DocumentCategory.FacturaComisionBolt,
        (Platform.Uber, PlatformDocumentType.CommissionInvoice) => DocumentCategory.FacturaComisionUber,
        (Platform.Bolt, PlatformDocumentType.PlatformReport) => DocumentCategory.RaportBolt,
        (Platform.Uber, PlatformDocumentType.PlatformReport) => DocumentCategory.RaportUber,
        _ => DocumentCategory.Other,
    };
}
