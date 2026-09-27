using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Registers;

/// <summary>Activele din Registrul-inventar (spec contabilitate B7, F6: CRUD simplu, fără ștergere).</summary>
internal static class Assets
{
    public static readonly Error NotFound = Error.NotFound("Accounting.AssetNotFound", "Activul nu există.");

    public static readonly Error Invalid = Error.Problem("Accounting.AssetInvalid", "Tipul și descrierea sunt obligatorii, iar valoarea achiziției trebuie să fie pozitivă.");

    public static readonly Error DisposedDateRequired = Error.Problem("Accounting.AssetInvalid", "Data ieșirii e obligatorie pentru un activ ieșit, și nu poate fi înainte de achiziție.");

    public static async Task<List<AssetDto>> DtosAsync(IApplicationDbContext db, IQueryable<PfaAsset> assets, CancellationToken cancellationToken)
    {
        var rows = await assets.AsNoTracking()
            .OrderBy(a => a.AcquisitionDate)
            .Select(a => new
            {
                Asset = a,
                Document = db.Documents.Where(d => d.Id == a.DocumentId).Select(d => new { d.Id, d.OriginalFileName, d.ContentType, d.FileSize }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new AssetDto(
            row.Asset.Id,
            row.Asset.PfaRegistrationId,
            row.Asset.Type,
            row.Asset.Description,
            row.Asset.AcquisitionDate,
            row.Asset.AcquisitionValue,
            row.Document is null ? null : new StoredFileRef(row.Document.Id, row.Document.OriginalFileName, row.Document.ContentType, row.Document.FileSize, string.Empty),
            row.Asset.Status,
            row.Asset.DisposedDate))];
    }

    public static Result Validate(AssetInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Type) || string.IsNullOrWhiteSpace(input.Description) || input.AcquisitionValue <= 0)
        {
            return Result.Failure(Invalid);
        }

        bool disposed = input.Status == AssetStatus.Disposed;
        return disposed && (input.DisposedDate is not { } date || date < input.AcquisitionDate)
            ? Result.Failure(DisposedDateRequired)
            : Result.Success();
    }

    public static void Apply(PfaAsset asset, AssetInput input)
    {
        asset.Type = input.Type.Trim();
        asset.Description = input.Description.Trim();
        asset.AcquisitionDate = input.AcquisitionDate;
        asset.AcquisitionValue = input.AcquisitionValue;
        asset.Status = input.Status;
        asset.DisposedDate = input.Status == AssetStatus.Disposed ? input.DisposedDate : null;
    }
}

/// <summary><c>GET /accounting/pfas/{pfaId}/assets</c></summary>
public sealed record ListAssetsQuery(Guid PfaId) : IQuery<IReadOnlyList<AssetDto>>;

internal sealed class ListAssetsQueryHandler(IApplicationDbContext db) : IQueryHandler<ListAssetsQuery, IReadOnlyList<AssetDto>>
{
    public async Task<Result<IReadOnlyList<AssetDto>>> Handle(ListAssetsQuery query, CancellationToken cancellationToken) =>
        await db.PfaRegistrations.AnyAsync(p => p.Id == query.PfaId, cancellationToken)
            ? await Assets.DtosAsync(db, db.PfaAssets.Where(a => a.PfaRegistrationId == query.PfaId), cancellationToken)
            : Result.Failure<IReadOnlyList<AssetDto>>(AccountingErrors.PfaNotFound);
}

/// <summary>
/// <c>POST /accounting/pfas/{pfaId}/assets</c> și <c>PUT …/assets/{id}</c>. Un activ nu se șterge:
/// iese din folosință (<c>DISPOSED</c>, cu data ieșirii). Auditul îl scrie interceptorul.
/// </summary>
public sealed record SaveAssetCommand(Guid PfaId, Guid? Id, AssetInput Input) : ICommand<AssetDto>;

internal sealed class SaveAssetCommandHandler(IApplicationDbContext db) : ICommandHandler<SaveAssetCommand, AssetDto>
{
    public async Task<Result<AssetDto>> Handle(SaveAssetCommand command, CancellationToken cancellationToken)
    {
        Result valid = Assets.Validate(command.Input);
        if (valid.IsFailure)
        {
            return Result.Failure<AssetDto>(valid.Error);
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

        PfaAsset? asset;
        if (command.Id is { } id)
        {
            asset = await db.PfaAssets.SingleOrDefaultAsync(a => a.Id == id && a.PfaRegistrationId == command.PfaId, cancellationToken);
            if (asset is null)
            {
                return Result.Failure<AssetDto>(Assets.NotFound);
            }
        }
        else
        {
            asset = new PfaAsset { Id = Guid.NewGuid(), PfaRegistrationId = command.PfaId };
            db.PfaAssets.Add(asset);
        }

        Assets.Apply(asset, command.Input);
        await db.SaveChangesAsync(cancellationToken);
        return (await Assets.DtosAsync(db, db.PfaAssets.Where(a => a.Id == asset.Id), cancellationToken)).Single();
    }
}
