using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.FiscalRegister;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Periods;

/// <summary>
/// Panoul de stare al registrelor unui PFA (spec registre §8): RJIP fără excepții, REF calculat,
/// inventarul de confirmat, activele de clasificat. Fiecare insignă duce la lista ei de acțiuni.
/// </summary>
/// <param name="RjipExceptions">Tranzacții fără document, de verificat sau payout-uri nereconciliate.</param>
/// <param name="Inventory">Ultima inventariere care nu e finală, dacă există.</param>
/// <param name="AssetsInClassification">Achiziții „posibil mijloc fix” fără decizie + active fără clasificare.</param>
public sealed record RegisterStatusDto(
    Guid PfaId,
    int Year,
    bool RjipOk,
    int RjipExceptions,
    RefStatus RefStatus,
    decimal RefNet,
    InventoryStatus? Inventory,
    Guid? InventoryCountId,
    int AssetsInClassification,
    AccountingPeriodStatus YearStatus);

/// <summary><c>GET /accounting/pfas/{pfaId}/registers/status?year=</c></summary>
public sealed record GetRegisterStatusQuery(Guid PfaId, int Year) : IQuery<RegisterStatusDto>;

internal sealed class GetRegisterStatusQueryHandler(IApplicationDbContext db, IQueryHandler<GetRefQuery, RefView> refQuery)
    : IQueryHandler<GetRegisterStatusQuery, RegisterStatusDto>
{
    public async Task<Result<RegisterStatusDto>> Handle(GetRegisterStatusQuery query, CancellationToken cancellationToken)
    {
        Result<RefView> refView = await refQuery.Handle(new GetRefQuery(query.PfaId, query.Year), cancellationToken);
        if (refView.IsFailure)
        {
            return Result.Failure<RegisterStatusDto>(refView.Error);
        }

        var start = new DateOnly(query.Year, 1, 1);
        var end = new DateOnly(query.Year, 12, 31);
        int exceptions = await db.LedgerEntries.AsNoTracking().CountAsync(
            e => e.PfaRegistrationId == query.PfaId && e.Date >= start && e.Date <= end && e.StornoOfEntryId == null && !e.ClosedPeriodFlag &&
                 (e.ReconciliationStatus == ReconciliationStatus.Unmatched ||
                  e.ReconciliationStatus == ReconciliationStatus.NeedsReview ||
                  e.TransactionType == LedgerTransactionType.PlatformSettlement),
            cancellationToken);

        var inventory = await db.InventoryCounts.AsNoTracking()
            .Where(c => c.PfaRegistrationId == query.PfaId && c.Status != InventoryStatus.Final)
            .OrderByDescending(c => c.Date)
            .Select(c => new { c.Id, c.Status })
            .FirstOrDefaultAsync(cancellationToken);

        int pending = await db.LedgerEntries.AsNoTracking()
            .CountAsync(e => e.PfaRegistrationId == query.PfaId && e.FixedAssetReview == FixedAssetReview.Pending, cancellationToken);
        int incomplete = await db.PfaAssets.AsNoTracking()
            .CountAsync(a => a.PfaRegistrationId == query.PfaId && a.Status == AssetStatus.PendingClassification, cancellationToken);

        AccountingPeriodStatus year = await db.AccountingYears.AsNoTracking()
            .Where(y => y.PfaRegistrationId == query.PfaId && y.Year == query.Year)
            .Select(y => (AccountingPeriodStatus?)y.Status)
            .FirstOrDefaultAsync(cancellationToken) ?? AccountingPeriodStatus.Open;

        RefRow net = refView.Value.Rows[^1];
        return new RegisterStatusDto(
            query.PfaId,
            query.Year,
            exceptions == 0,
            exceptions,
            refView.Value.Status,
            net.CalculationElement == "Pierdere netă anuală" ? -net.Value : net.Value,
            inventory?.Status,
            inventory?.Id,
            pending + incomplete,
            year);
    }
}
