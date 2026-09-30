using System.Globalization;
using System.IO.Compression;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Assets;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.FiscalRegister;
using Application.Accounting.Inventory;
using Application.Accounting.Ledger;
using Application.Accounting.Pfas;
using Application.Accounting.Registers;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Periods;

public sealed record AccountingYearDto(
    Guid PfaId,
    int Year,
    AccountingPeriodStatus Status,
    UserRef? ClosedBy,
    DateTime? ClosedAt,
    bool HasPackage,
    IReadOnlyList<string> Missing);

internal static class YearErrors
{
    public static Error NotReady(IEnumerable<string> missing) =>
        Error.Conflict("Accounting.YearNotReady", $"Anul nu se poate închide: {string.Join(" ", missing)}");

    public static Error AlreadyClosed(int year) => Error.Conflict("Accounting.YearClosed", $"Anul {year} e deja închis.");

    public static Error NotClosed(int year) => Error.Conflict("Accounting.YearOpen", $"Anul {year} nu e închis.");

    public static readonly Error NoPackage = Error.NotFound("Accounting.YearPackageNotFound", "Pachetul anual nu există: anul nu e închis.");
}

/// <summary>Ce lipsește pentru închiderea anului (spec registre §7): lunile deschise și inventarul nefinal.</summary>
internal static class AccountingYearChecks
{
    public static async Task<List<string>> MissingAsync(IApplicationDbContext db, Guid pfaId, int year, CancellationToken cancellationToken)
    {
        List<string> missing = [];
        EngagementInfo? engagement = await PfaEngagements.InfoAsync(db, pfaId, cancellationToken);
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 1);
        if (engagement is not null && engagement.StartDate > first)
        {
            first = new DateOnly(engagement.StartDate.Year, engagement.StartDate.Month, 1);
        }

        if (engagement?.EndDate is { } end && end < last.AddMonths(1))
        {
            last = new DateOnly(end.Year, end.Month, 1);
        }

        HashSet<string> closed = [.. await db.PfaAccountingPeriods.AsNoTracking()
            .Where(p => p.PfaRegistrationId == pfaId && p.Status == AccountingPeriodStatus.Closed && p.Period.StartsWith(year.ToString(CultureInfo.InvariantCulture)))
            .Select(p => p.Period)
            .ToListAsync(cancellationToken)];
        for (DateOnly month = first; month <= last; month = month.AddMonths(1))
        {
            if (!closed.Contains(LedgerSupport.PeriodOf(month)))
            {
                missing.Add($"Luna {month.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ro-RO"))} nu e închisă.");
            }
        }

        bool inventory = await db.InventoryCounts.AnyAsync(
            c => c.PfaRegistrationId == pfaId && c.Date.Year == year && c.Reason != InventoryReason.ActivityStart && c.Status == InventoryStatus.Final,
            cancellationToken);
        if (!inventory)
        {
            missing.Add("Inventarul de la sfârșitul anului nu e final.");
        }

        return missing;
    }

    public static async Task<AccountingYearDto> DtoAsync(IApplicationDbContext db, Guid pfaId, int year, CancellationToken cancellationToken)
    {
        AccountingYear? row = await db.AccountingYears.AsNoTracking().SingleOrDefaultAsync(y => y.PfaRegistrationId == pfaId && y.Year == year, cancellationToken);
        Dictionary<Guid, UserRef> users = await Documents.PlatformDocumentSupport.UsersAsync(db, [row?.ClosedByUserId], cancellationToken);
        AccountingPeriodStatus status = row?.Status ?? AccountingPeriodStatus.Open;
        return new AccountingYearDto(
            pfaId,
            year,
            status,
            row?.ClosedByUserId is { } by ? users.GetValueOrDefault(by) : null,
            row?.ClosedAtUtc,
            row?.PackageDocumentId is not null,
            status == AccountingPeriodStatus.Closed ? [] : await MissingAsync(db, pfaId, year, cancellationToken));
    }
}

/// <summary><c>GET /accounting/pfas/{pfaId}/years/{year}</c> — starea anului și ce lipsește pentru închidere.</summary>
public sealed record GetAccountingYearQuery(Guid PfaId, int Year) : IQuery<AccountingYearDto>;

internal sealed class GetAccountingYearQueryHandler(IApplicationDbContext db) : IQueryHandler<GetAccountingYearQuery, AccountingYearDto>
{
    public async Task<Result<AccountingYearDto>> Handle(GetAccountingYearQuery query, CancellationToken cancellationToken) =>
        await db.PfaRegistrations.AnyAsync(p => p.Id == query.PfaId, cancellationToken)
            ? await AccountingYearChecks.DtoAsync(db, query.PfaId, query.Year, cancellationToken)
            : Result.Failure<AccountingYearDto>(AccountingErrors.PfaNotFound);
}

/// <summary>
/// <c>POST /accounting/pfas/{pfaId}/years/{year}/close</c> — „Închide anul” (spec registre §7): 12/12
/// luni închise și inventarul final; REF-ul final și pachetul anual (RJIP, REF, Registru-inventar,
/// fișele MF, lista activelor) se salvează; anul devine <c>CLOSED</c>.
/// </summary>
public sealed record CloseAccountingYearCommand(Guid PfaId, int Year) : ICommand<AccountingYearDto>;

internal sealed class CloseAccountingYearCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    IQueryHandler<ExportRjipQuery, RegisterFile>? rjip = null,
    IQueryHandler<ExportRefQuery, RegisterFile>? refExport = null,
    IQueryHandler<ExportInventoryQuery, RegisterFile>? inventory = null,
    IQueryHandler<ExportAssetSheetQuery, RegisterFile>? sheets = null,
    IQueryHandler<ExportAssetListQuery, RegisterFile>? assetList = null,
    DeclarationFiles? files = null)
    : ICommandHandler<CloseAccountingYearCommand, AccountingYearDto>
{
    public async Task<Result<AccountingYearDto>> Handle(CloseAccountingYearCommand command, CancellationToken cancellationToken)
    {
        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == command.PfaId, cancellationToken))
        {
            return Result.Failure<AccountingYearDto>(AccountingErrors.PfaNotFound);
        }

        AccountingYear? year = await db.AccountingYears.SingleOrDefaultAsync(y => y.PfaRegistrationId == command.PfaId && y.Year == command.Year, cancellationToken);
        if (year is { Status: AccountingPeriodStatus.Closed })
        {
            return Result.Failure<AccountingYearDto>(YearErrors.AlreadyClosed(command.Year));
        }

        List<string> missing = await AccountingYearChecks.MissingAsync(db, command.PfaId, command.Year, cancellationToken);
        if (missing.Count > 0)
        {
            return Result.Failure<AccountingYearDto>(YearErrors.NotReady(missing));
        }

        // Fișele MF la 31.12: activele cu planul terminat în an sunt amortizate integral.
        var yearEnd = new DateOnly(command.Year, 12, 31);
        List<PfaAsset> active = await db.PfaAssets
            .Where(a => a.PfaRegistrationId == command.PfaId && a.Status == AssetStatus.Active && a.Kind == AssetKind.FixedAsset)
            .ToListAsync(cancellationToken);
        foreach (PfaAsset asset in active)
        {
            List<DepreciationLine> lines = await db.DepreciationLines.AsNoTracking().Where(l => l.AssetId == asset.Id).ToListAsync(cancellationToken);
            if (lines.Count > 0 && lines.All(l => l.Year <= command.Year) && Depreciation.AccumulatedAt(lines, yearEnd) == asset.EntryValue)
            {
                asset.Status = AssetStatus.FullyDepreciated;
            }
        }

        RefView refView = await GetRefQueryHandler.ComputeAsync(db, command.PfaId, command.Year, RefStatus.Final, null, cancellationToken);
        if (year is null)
        {
            year = new AccountingYear { Id = Guid.NewGuid(), PfaRegistrationId = command.PfaId, Year = command.Year };
            db.AccountingYears.Add(year);
        }

        year.Status = AccountingPeriodStatus.Closed;
        year.ClosedAtUtc = DateTime.UtcNow;
        year.ClosedByUserId = userContext.UserId;
        year.RefJson = AccountingJson.Serialize(refView);
        AccountingAudit.Record(db, command.PfaId, nameof(AccountingYear), year.Id, "CLOSE",
            new { status = AccountingPeriodStatus.Open }, new { status = AccountingPeriodStatus.Closed, net = refView.Rows[^1].Value }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);

        // Pachetul se face din anul deja închis: REF-ul din el e cel final.
        year.PackageDocumentId = await PackageAsync(command.PfaId, command.Year, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await AccountingYearChecks.DtoAsync(db, command.PfaId, command.Year, cancellationToken);
    }

    /// <summary>Pachetul anual: ZIP cu PDF-urile registrelor, stocat ca document al PFA-ului.</summary>
    private async Task<Guid?> PackageAsync(Guid pfaId, int year, CancellationToken cancellationToken)
    {
        if (rjip is null || refExport is null || inventory is null || sheets is null || assetList is null || files is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var yearEnd = new DateOnly(year, 12, 31);
            await AddAsync(zip, rjip.Handle(new ExportRjipQuery(pfaId, new DateOnly(year, 1, 1), yearEnd, RegisterFormat.Pdf), cancellationToken), cancellationToken);
            await AddAsync(zip, refExport.Handle(new ExportRefQuery(pfaId, year, RegisterFormat.Pdf, null), cancellationToken), cancellationToken);
            await AddAsync(zip, inventory.Handle(new ExportInventoryQuery(pfaId, year, RegisterFormat.Pdf), cancellationToken), cancellationToken);
            await AddAsync(zip, assetList.Handle(new ExportAssetListQuery(pfaId, yearEnd, RegisterFormat.Pdf), cancellationToken), cancellationToken);
            List<Guid> fixedAssets = await db.PfaAssets.AsNoTracking()
                .Where(a => a.PfaRegistrationId == pfaId && a.Kind == AssetKind.FixedAsset && a.EntryDate <= yearEnd)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);
            foreach (Guid id in fixedAssets)
            {
                await AddAsync(zip, sheets.Handle(new ExportAssetSheetQuery(pfaId, id, RegisterFormat.Pdf), cancellationToken), cancellationToken, "Fise_MF/");
            }
        }

        string cui = (await RegisterData.PfaAsync(db, pfaId, cancellationToken))?.Cui ?? pfaId.ToString("N");
        return (await files.StoreAsync(pfaId, buffer.ToArray(), $"RIDElance_Registre_{cui}_{year}.zip", "application/zip", cancellationToken)).Id;
    }

    private static async Task AddAsync(ZipArchive zip, Task<Result<RegisterFile>> export, CancellationToken cancellationToken, string folder = "")
    {
        Result<RegisterFile> file = await export;
        if (file.IsFailure)
        {
            return;
        }

        ZipArchiveEntry entry = zip.CreateEntry(folder + file.Value.FileName, CompressionLevel.Optimal);
        await using Stream stream = await entry.OpenAsync(cancellationToken);
        await stream.WriteAsync(file.Value.Content, cancellationToken);
    }
}

/// <summary><c>POST /accounting/pfas/{pfaId}/years/{year}/reopen</c> — doar ADMIN, cu motiv și audit.</summary>
public sealed record ReopenAccountingYearCommand(Guid PfaId, int Year, string? Reason) : ICommand<AccountingYearDto>;

internal sealed class ReopenAccountingYearCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<ReopenAccountingYearCommand, AccountingYearDto>
{
    public async Task<Result<AccountingYearDto>> Handle(ReopenAccountingYearCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<AccountingYearDto>(PeriodErrors.ReopenReasonRequired);
        }

        AccountingYear? year = await db.AccountingYears.SingleOrDefaultAsync(y => y.PfaRegistrationId == command.PfaId && y.Year == command.Year, cancellationToken);
        if (year is not { Status: AccountingPeriodStatus.Closed })
        {
            return Result.Failure<AccountingYearDto>(YearErrors.NotClosed(command.Year));
        }

        year.Status = AccountingPeriodStatus.Open;
        AccountingAudit.Record(db, command.PfaId, nameof(AccountingYear), year.Id, "REOPEN",
            new { status = AccountingPeriodStatus.Closed }, new { status = AccountingPeriodStatus.Open }, command.Reason.Trim(), userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await AccountingYearChecks.DtoAsync(db, command.PfaId, command.Year, cancellationToken);
    }
}

/// <summary><c>GET …/years/{year}/package</c> (Admin) și <c>GET /pfa/years/{year}/package</c> (PFA-ul logat).</summary>
public sealed record GetYearPackageQuery(Guid? PfaId, int Year) : IQuery<RegisterFile>;

internal sealed class GetYearPackageQueryHandler(IApplicationDbContext db, IUserContext userContext, DeclarationFiles files)
    : IQueryHandler<GetYearPackageQuery, RegisterFile>
{
    public async Task<Result<RegisterFile>> Handle(GetYearPackageQuery query, CancellationToken cancellationToken)
    {
        Guid? pfaId = query.PfaId ?? await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken);
        if (pfaId is null)
        {
            return Result.Failure<RegisterFile>(ClientLedger.NoPfa);
        }

        Guid? documentId = await db.AccountingYears.AsNoTracking()
            .Where(y => y.PfaRegistrationId == pfaId && y.Year == query.Year && y.Status == AccountingPeriodStatus.Closed)
            .Select(y => y.PackageDocumentId)
            .FirstOrDefaultAsync(cancellationToken);
        return await files.ReadAsync(documentId, cancellationToken) is { } stored
            ? new RegisterFile(stored.Content, stored.Document.OriginalFileName, "application/zip")
            : Result.Failure<RegisterFile>(YearErrors.NoPackage);
    }
}

/// <summary><c>GET /pfa/years</c> — anii PFA-ului logat, cu pachetul anual al celor închise.</summary>
public sealed record GetClientYearsQuery : IQuery<IReadOnlyList<AccountingYearDto>>;

internal sealed class GetClientYearsQueryHandler(IApplicationDbContext db, IUserContext userContext)
    : IQueryHandler<GetClientYearsQuery, IReadOnlyList<AccountingYearDto>>
{
    public async Task<Result<IReadOnlyList<AccountingYearDto>>> Handle(GetClientYearsQuery query, CancellationToken cancellationToken)
    {
        if (await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is not { } pfaId)
        {
            return Result.Failure<IReadOnlyList<AccountingYearDto>>(ClientLedger.NoPfa);
        }

        List<int> years = await db.AccountingYears.AsNoTracking()
            .Where(y => y.PfaRegistrationId == pfaId && y.Status == AccountingPeriodStatus.Closed)
            .OrderByDescending(y => y.Year)
            .Select(y => y.Year)
            .ToListAsync(cancellationToken);
        List<AccountingYearDto> dtos = [];
        foreach (int year in years)
        {
            dtos.Add(await AccountingYearChecks.DtoAsync(db, pfaId, year, cancellationToken));
        }

        return dtos;
    }
}
