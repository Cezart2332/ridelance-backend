using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Application.Accounting.Ledger;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Assets;

internal static class AssetErrors
{
    public static readonly Error NotFound = Error.NotFound("Accounting.AssetNotFound", "Activul nu există.");

    public static readonly Error EntryNotFound = Error.NotFound("Accounting.LedgerEntryNotFound", "Plata nu există.");

    public static readonly Error NotACandidate = Error.Conflict(
        "Accounting.FixedAssetDecided", "Plata nu e o achiziție de clasificat: decizia e deja luată sau nu e o cheltuială.");

    public static readonly Error InvalidDecision = Error.Problem(
        "Accounting.FixedAssetDecisionInvalid", "Decizia e EXPENSE, FIXED_ASSET sau INVENTORY_OBJECT.");

    public static readonly Error Invalid = Error.Problem(
        "Accounting.AssetInvalid", "Denumirea și documentul sunt obligatorii, iar valoarea de intrare trebuie să fie pozitivă.");

    public static readonly Error LifeInvalid = Error.Problem(
        "Accounting.AssetInvalid", "Durata normală de funcționare e în luni, între 12 și 600.");

    public static readonly Error InServiceBeforeEntry = Error.Problem(
        "Accounting.AssetInvalid", "Punerea în funcțiune nu poate fi înainte de data intrării.");

    public static readonly Error DisposalBeforeEntry = Error.Problem(
        "Accounting.AssetInvalid", "Ieșirea din gestiune nu poate fi înainte de data intrării.");

    public static readonly Error DisposalBeforeLockedMonths = Error.Conflict(
        "Accounting.AssetDisposalInClosedMonth", "Activul are amortizare în luni închise după data ieșirii.");

    public static readonly Error AlreadyDisposed = Error.Conflict("Accounting.AssetDisposed", "Activul e deja ieșit din gestiune.");
}

internal static class AssetSupport
{
    public static async Task<List<AssetDto>> DtosAsync(IApplicationDbContext db, IQueryable<PfaAsset> query, DateOnly asOf, CancellationToken cancellationToken)
    {
        List<PfaAsset> assets = await query.AsNoTracking().OrderBy(a => a.InventoryNumber).ToListAsync(cancellationToken);
        List<Guid> ids = [.. assets.Select(a => a.Id)];
        List<Guid> documentIds = [.. assets.Where(a => a.DocumentId != null).Select(a => a.DocumentId!.Value)];
        Dictionary<Guid, StoredFileRef> documents = await db.Documents.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => new StoredFileRef(d.Id, d.OriginalFileName, d.ContentType, d.FileSize, string.Empty), cancellationToken);
        ILookup<Guid, DepreciationLine> lines = (await db.DepreciationLines.AsNoTracking()
            .Where(l => ids.Contains(l.AssetId))
            .ToListAsync(cancellationToken)).ToLookup(l => l.AssetId);
        return [.. assets.Select(asset => Dto(asset, [.. lines[asset.Id]], asOf, asset.DocumentId is { } id ? documents.GetValueOrDefault(id) : null))];
    }

    public static AssetDto Dto(PfaAsset asset, List<DepreciationLine> lines, DateOnly asOf, StoredFileRef? document)
    {
        decimal accumulated = asset.Kind == AssetKind.FixedAsset ? Depreciation.AccumulatedAt(lines, asOf) : 0;
        decimal remaining = asset.EntryValue - accumulated;
        // QA 19: fără punere în funcțiune (sau clasă, durată) activul e încă în clasificare, oricare ar fi starea salvată.
        AssetStatus status = asset.Status;
        if (status == AssetStatus.Active && !asset.IsComplete)
        {
            status = AssetStatus.PendingClassification;
        }
        else if (status == AssetStatus.Active && lines.Count > 0 && remaining == 0)
        {
            status = AssetStatus.FullyDepreciated;
        }

        decimal? monthly = lines.Count == 0 ? null : lines.OrderBy(l => l.Year).ThenBy(l => l.Month).First().Amount;
        return new AssetDto(
            asset.Id, asset.PfaRegistrationId, asset.InventoryNumber, CleanName(asset.Name), asset.Kind, status, asset.AcquisitionEntryId,
            asset.DocumentRef, asset.SupplierName, asset.EntryDate, asset.InServiceDate, asset.EntryValue, asset.DepreciationClassCode,
            asset.NormalLifeMonths, asset.Method, asset.DisposalDate, asset.DisposalReason, document, monthly, asOf, accumulated, remaining);
    }

    public static async Task<AssetDto> DtoAsync(IApplicationDbContext db, Guid assetId, DateOnly asOf, CancellationToken cancellationToken) =>
        (await DtosAsync(db, db.PfaAssets.Where(a => a.Id == assetId), asOf, cancellationToken)).Single();

    /// <summary><c>MF-0001</c> / <c>OI-0001</c>: secvență per PFA și fel, fără reutilizarea numerelor.</summary>
    public static async Task<string> NextNumberAsync(IApplicationDbContext db, Guid pfaId, AssetKind kind, CancellationToken cancellationToken)
    {
        string prefix = kind == AssetKind.FixedAsset ? "MF-" : "OI-";
        List<string> numbers = await db.PfaAssets.AsNoTracking()
            .Where(a => a.PfaRegistrationId == pfaId && a.InventoryNumber.StartsWith(prefix))
            .Select(a => a.InventoryNumber)
            .ToListAsync(cancellationToken);
        int next = numbers
            .Select(n => int.TryParse(n.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"{prefix}{next:0000}";
    }

    /// <summary>Starea după completare: <c>Active</c> doar cu clasă, durată și punere în funcțiune (§6 pas 5).</summary>
    public static void RefreshStatus(PfaAsset asset)
    {
        if (asset.Status != AssetStatus.Disposed)
        {
            asset.Status = asset.IsComplete ? AssetStatus.Active : AssetStatus.PendingClassification;
        }
    }

    public static DateOnly Today(IDateTimeProvider? clock) => DateOnly.FromDateTime(clock?.UtcNow ?? DateTime.UtcNow);

    /// <summary>
    /// Denumirea fără separatori rămași de la câmpuri goale („Laptop Acer — .” → „Laptop Acer”, QA 19).
    /// </summary>
    public static string CleanName(string? name)
    {
        string text = (name ?? string.Empty).Trim();
        string previous;
        do
        {
            previous = text;
            text = text.TrimEnd(' ', '.', ',', ';', ':', '-', '–', '—').Trim();
        }
        while (text != previous);

        return text.Length == 0 ? (name ?? string.Empty).Trim() : text;
    }
}

// ─── Liste ───────────────────────────────────────────────────────────────────────────────────────

/// <summary><c>GET /accounting/pfas/{pfaId}/assets?asOf=</c> — lista activelor (registrul imobilizărilor).</summary>
public sealed record ListAssetsQuery(Guid PfaId, DateOnly? AsOf = null) : IQuery<IReadOnlyList<AssetDto>>;

internal sealed class ListAssetsQueryHandler(IApplicationDbContext db, IDateTimeProvider? clock = null) : IQueryHandler<ListAssetsQuery, IReadOnlyList<AssetDto>>
{
    public async Task<Result<IReadOnlyList<AssetDto>>> Handle(ListAssetsQuery query, CancellationToken cancellationToken) =>
        await db.PfaRegistrations.AnyAsync(p => p.Id == query.PfaId, cancellationToken)
            ? await AssetSupport.DtosAsync(db, db.PfaAssets.Where(a => a.PfaRegistrationId == query.PfaId), query.AsOf ?? AssetSupport.Today(clock), cancellationToken)
            : Result.Failure<IReadOnlyList<AssetDto>>(AccountingErrors.PfaNotFound);
}

/// <summary><c>GET /accounting/pfas/{pfaId}/assets/{id}</c> — activul cu planul de amortizare.</summary>
public sealed record GetAssetQuery(Guid PfaId, Guid AssetId) : IQuery<AssetDetailDto>;

internal sealed class GetAssetQueryHandler(IApplicationDbContext db, IDateTimeProvider? clock = null) : IQueryHandler<GetAssetQuery, AssetDetailDto>
{
    public async Task<Result<AssetDetailDto>> Handle(GetAssetQuery query, CancellationToken cancellationToken)
    {
        List<AssetDto> assets = await AssetSupport.DtosAsync(
            db, db.PfaAssets.Where(a => a.Id == query.AssetId && a.PfaRegistrationId == query.PfaId), AssetSupport.Today(clock), cancellationToken);
        if (assets.Count == 0)
        {
            return Result.Failure<AssetDetailDto>(AssetErrors.NotFound);
        }

        List<DepreciationLineDto> lines = await db.DepreciationLines.AsNoTracking()
            .Where(l => l.AssetId == query.AssetId)
            .OrderBy(l => l.Year).ThenBy(l => l.Month)
            .Select(l => new DepreciationLineDto(l.Year, l.Month, l.Amount, l.Accumulated, l.Remaining, l.IsLocked))
            .ToListAsync(cancellationToken);
        return new AssetDetailDto(assets[0], lines);
    }
}

/// <summary><c>GET /accounting/pfas/{pfaId}/fixed-asset-candidates</c> — achizițiile care așteaptă decizia.</summary>
public sealed record ListFixedAssetCandidatesQuery(Guid PfaId) : IQuery<IReadOnlyList<FixedAssetCandidateDto>>;

internal sealed class ListFixedAssetCandidatesQueryHandler(IApplicationDbContext db)
    : IQueryHandler<ListFixedAssetCandidatesQuery, IReadOnlyList<FixedAssetCandidateDto>>
{
    public async Task<Result<IReadOnlyList<FixedAssetCandidateDto>>> Handle(ListFixedAssetCandidatesQuery query, CancellationToken cancellationToken) =>
        await db.LedgerEntries.AsNoTracking()
            .Where(e => e.PfaRegistrationId == query.PfaId && e.FixedAssetReview == FixedAssetReview.Pending)
            .OrderBy(e => e.Date)
            .Select(e => new FixedAssetCandidateDto(e.Id, e.Date, e.DocumentLabel, e.Description, e.Counterparty, e.Amount, e.Category, e.FixedAssetReview))
            .ToListAsync(cancellationToken);
}

// ─── Decizia Adminului ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>POST /accounting/pfas/{pfaId}/ledger/{entryId}/fixed-asset-decision</c> (spec registre §6 pașii
/// 3–4): cheltuială curentă (se deduce după categorie) sau activ. La activ, numărul de inventar și
/// datele vin din document (furnizor, document, dată, valoare); plata unui mijloc fix nu se deduce.
/// </summary>
public sealed record DecideFixedAssetCommand(Guid PfaId, Guid LedgerEntryId, FixedAssetReview Decision, string? Name, string? Reason) : ICommand<AssetDto?>;

internal sealed class DecideFixedAssetCommandHandler(IApplicationDbContext db, IUserContext userContext, IDateTimeProvider? clock = null)
    : ICommandHandler<DecideFixedAssetCommand, AssetDto?>
{
    public async Task<Result<AssetDto?>> Handle(DecideFixedAssetCommand command, CancellationToken cancellationToken)
    {
        if (command.Decision is not (FixedAssetReview.Expense or FixedAssetReview.FixedAsset or FixedAssetReview.InventoryObject))
        {
            return Result.Failure<AssetDto?>(AssetErrors.InvalidDecision);
        }

        LedgerEntry? entry = await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == command.LedgerEntryId && e.PfaRegistrationId == command.PfaId, cancellationToken);
        if (entry is null)
        {
            return Result.Failure<AssetDto?>(AssetErrors.EntryNotFound);
        }

        if (entry.TransactionType != LedgerTransactionType.Expense || entry.StornoOfEntryId is not null ||
            entry.FixedAssetReview is not (FixedAssetReview.None or FixedAssetReview.Pending))
        {
            return Result.Failure<AssetDto?>(AssetErrors.NotACandidate);
        }

        if (entry.Status == LedgerEntryStatus.Locked)
        {
            return Result.Failure<AssetDto?>(LedgerErrors.Locked);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, command.PfaId, entry.AccountingPeriod, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<AssetDto?>(writable.Error);
        }

        FixedAssetReview before = entry.FixedAssetReview;
        entry.FixedAssetReview = command.Decision;
        DeductibilityService.Resolve(entry, await LedgerSupport.RulesAsync(db, command.PfaId, cancellationToken));

        PfaAsset? asset = null;
        if (command.Decision != FixedAssetReview.Expense)
        {
            asset = await NewAssetAsync(entry, command, cancellationToken);
            db.PfaAssets.Add(asset);
        }

        AccountingAudit.Record(db, command.PfaId, nameof(LedgerEntry), entry.Id, "FIXED_ASSET_DECISION",
            new { fixedAssetReview = before }, new { fixedAssetReview = entry.FixedAssetReview, assetId = asset?.Id, entry.DeductibleAmount },
            command.Reason, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return asset is null
            ? Result.Success<AssetDto?>(null)
            : await AssetSupport.DtoAsync(db, asset.Id, AssetSupport.Today(clock), cancellationToken);
    }

    private async Task<PfaAsset> NewAssetAsync(LedgerEntry entry, DecideFixedAssetCommand command, CancellationToken cancellationToken)
    {
        AssetKind kind = command.Decision == FixedAssetReview.FixedAsset ? AssetKind.FixedAsset : AssetKind.InventoryObject;
        EFacturaMessage? invoice = entry.EFacturaMessageId is { } invoiceId
            ? await db.EFacturaMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == invoiceId, cancellationToken)
            : null;
        ExpenseDocument? receipt = await db.ExpenseDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.LedgerEntryId == entry.Id, cancellationToken);

        string documentRef = entry.DocumentLabel;
        if (invoice is { InvoiceNumber: { Length: > 0 } number })
        {
            documentRef = invoice.IssueDate is { } issued ? $"Factura {number} / {issued:dd.MM.yyyy}" : $"Factura {number}";
        }
        else if (receipt is { Number: { Length: > 0 } receiptNumber })
        {
            documentRef = $"Bon fiscal {receiptNumber}";
        }

        return new PfaAsset
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = entry.PfaRegistrationId,
            InventoryNumber = await AssetSupport.NextNumberAsync(db, entry.PfaRegistrationId, kind, cancellationToken),
            Name = LedgerSupport.Cut(AssetSupport.CleanName(string.IsNullOrWhiteSpace(command.Name) ? entry.Description : command.Name), 500),
            Kind = kind,
            Status = AssetStatus.PendingClassification,
            AcquisitionEntryId = entry.Id,
            DocumentRef = LedgerSupport.Cut(documentRef, 256),
            SupplierName = invoice?.SupplierName ?? receipt?.Merchant ?? entry.Counterparty,
            EntryDate = invoice?.IssueDate ?? receipt?.Date ?? entry.DocumentDate ?? entry.Date,
            EntryValue = entry.BusinessAmount,
            DocumentId = entry.SourceDocumentId ?? receipt?.DocumentId ?? invoice?.PdfDocumentId,
            CreatedByUserId = userContext.UserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }
}

/// <summary><c>POST /accounting/pfas/{pfaId}/assets</c> — un bun adus ca aport, fără plată în ledger.</summary>
public sealed record CreateManualAssetCommand(Guid PfaId, ManualAssetRequest Request) : ICommand<AssetDto>;

internal sealed class CreateManualAssetCommandHandler(IApplicationDbContext db, IUserContext userContext, IDateTimeProvider? clock = null)
    : ICommandHandler<CreateManualAssetCommand, AssetDto>
{
    public async Task<Result<AssetDto>> Handle(CreateManualAssetCommand command, CancellationToken cancellationToken)
    {
        ManualAssetRequest request = command.Request;
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.DocumentRef) || request.EntryValue <= 0 || !Enum.IsDefined(request.Kind))
        {
            return Result.Failure<AssetDto>(AssetErrors.Invalid);
        }

        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == command.PfaId, cancellationToken))
        {
            return Result.Failure<AssetDto>(AccountingErrors.PfaNotFound);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, command.PfaId, string.Empty, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<AssetDto>(writable.Error);
        }

        var asset = new PfaAsset
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = command.PfaId,
            InventoryNumber = await AssetSupport.NextNumberAsync(db, command.PfaId, request.Kind, cancellationToken),
            Name = AssetSupport.CleanName(request.Name),
            Kind = request.Kind,
            Status = AssetStatus.PendingClassification,
            DocumentRef = request.DocumentRef.Trim(),
            SupplierName = request.SupplierName?.Trim(),
            EntryDate = request.EntryDate,
            EntryValue = LedgerInvariants.Round(request.EntryValue),
            CreatedByUserId = userContext.UserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.PfaAssets.Add(asset);
        AccountingAudit.Record(db, command.PfaId, nameof(PfaAsset), asset.Id, "CREATE", null, request, request.Reason, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await AssetSupport.DtoAsync(db, asset.Id, AssetSupport.Today(clock), cancellationToken);
    }
}

/// <summary>
/// <c>PUT /accounting/pfas/{pfaId}/assets/{id}</c> (spec registre §6 pas 5, editări): denumire,
/// document, clasă, durată, punere în funcțiune. Un mijloc fix devine activ cu toate trei; planul
/// se recalculează doar pe lunile deschise.
/// </summary>
public sealed record ClassifyAssetCommand(Guid PfaId, Guid AssetId, AssetClassificationRequest Request) : ICommand<AssetDto>;

internal sealed class ClassifyAssetCommandHandler(IApplicationDbContext db, IUserContext userContext, IDateTimeProvider? clock = null)
    : ICommandHandler<ClassifyAssetCommand, AssetDto>
{
    public async Task<Result<AssetDto>> Handle(ClassifyAssetCommand command, CancellationToken cancellationToken)
    {
        AssetClassificationRequest request = command.Request;
        PfaAsset? asset = await db.PfaAssets.SingleOrDefaultAsync(a => a.Id == command.AssetId && a.PfaRegistrationId == command.PfaId, cancellationToken);
        if (asset is null)
        {
            return Result.Failure<AssetDto>(AssetErrors.NotFound);
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.DocumentRef))
        {
            return Result.Failure<AssetDto>(AssetErrors.Invalid);
        }

        if (request.NormalLifeMonths is { } life && life is < 12 or > 600)
        {
            return Result.Failure<AssetDto>(AssetErrors.LifeInvalid);
        }

        if (request.InServiceDate < asset.EntryDate)
        {
            return Result.Failure<AssetDto>(AssetErrors.InServiceBeforeEntry);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, command.PfaId, string.Empty, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<AssetDto>(writable.Error);
        }

        var before = new { asset.Name, asset.DocumentRef, asset.SupplierName, asset.InServiceDate, asset.DepreciationClassCode, asset.NormalLifeMonths, asset.Status };
        asset.Name = AssetSupport.CleanName(request.Name);
        asset.DocumentRef = request.DocumentRef.Trim();
        asset.SupplierName = request.SupplierName?.Trim();
        asset.InServiceDate = request.InServiceDate;
        asset.DepreciationClassCode = string.IsNullOrWhiteSpace(request.DepreciationClassCode) ? null : request.DepreciationClassCode.Trim();
        asset.NormalLifeMonths = asset.Kind == AssetKind.FixedAsset ? request.NormalLifeMonths : null;
        AssetSupport.RefreshStatus(asset);

        Result rebuilt = await Depreciation.RebuildAsync(db, asset, cancellationToken);
        if (rebuilt.IsFailure)
        {
            return Result.Failure<AssetDto>(rebuilt.Error);
        }

        AccountingAudit.Record(db, command.PfaId, nameof(PfaAsset), asset.Id, "CLASSIFY", before,
            new { asset.Name, asset.DocumentRef, asset.SupplierName, asset.InServiceDate, asset.DepreciationClassCode, asset.NormalLifeMonths, asset.Status },
            request.Reason, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await AssetSupport.DtoAsync(db, asset.Id, AssetSupport.Today(clock), cancellationToken);
    }
}

/// <summary>
/// <c>POST /accounting/pfas/{pfaId}/assets/{id}/dispose</c> — ieșirea din gestiune: amortizarea se
/// oprește din luna următoare; liniile viitoare dispar, cele din lunile închise rămân.
/// </summary>
public sealed record DisposeAssetCommand(Guid PfaId, Guid AssetId, AssetDisposalRequest Request) : ICommand<AssetDto>;

internal sealed class DisposeAssetCommandHandler(IApplicationDbContext db, IUserContext userContext, IDateTimeProvider? clock = null)
    : ICommandHandler<DisposeAssetCommand, AssetDto>
{
    public async Task<Result<AssetDto>> Handle(DisposeAssetCommand command, CancellationToken cancellationToken)
    {
        PfaAsset? asset = await db.PfaAssets.SingleOrDefaultAsync(a => a.Id == command.AssetId && a.PfaRegistrationId == command.PfaId, cancellationToken);
        if (asset is null)
        {
            return Result.Failure<AssetDto>(AssetErrors.NotFound);
        }

        if (asset.Status == AssetStatus.Disposed)
        {
            return Result.Failure<AssetDto>(AssetErrors.AlreadyDisposed);
        }

        if (command.Request.Date < asset.EntryDate)
        {
            return Result.Failure<AssetDto>(AssetErrors.DisposalBeforeEntry);
        }

        if (string.IsNullOrWhiteSpace(command.Request.Reason))
        {
            return Result.Failure<AssetDto>(AccountingErrors.ReasonRequired);
        }

        asset.Status = AssetStatus.Disposed;
        asset.DisposalDate = command.Request.Date;
        asset.DisposalReason = command.Request.Reason.Trim();
        Result rebuilt = await Depreciation.RebuildAsync(db, asset, cancellationToken);
        if (rebuilt.IsFailure)
        {
            return Result.Failure<AssetDto>(rebuilt.Error);
        }

        AccountingAudit.Record(db, command.PfaId, nameof(PfaAsset), asset.Id, "DISPOSE", null,
            new { asset.DisposalDate, asset.DisposalReason }, asset.DisposalReason, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await AssetSupport.DtoAsync(db, asset.Id, AssetSupport.Today(clock), cancellationToken);
    }
}
