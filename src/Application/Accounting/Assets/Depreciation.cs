using Application.Abstractions.Data;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Assets;

/// <summary>
/// Amortizarea liniară a mijloacelor fixe (spec registre §6): <c>EntryValue / NormalLifeMonths</c> pe
/// lună, diferența de rotunjire în ultima lună, din luna dată de <see cref="FixedAssetRule"/>. Liniile
/// lunilor închise rămân neatinse; o modificare recalculează doar lunile deschise.
/// </summary>
internal static class Depreciation
{
    public static readonly Error YearClosed = Error.Conflict(
        "Accounting.YearClosed", "Amortizarea ar schimba un an închis; anul se redeschide întâi.");

    public readonly record struct PlannedLine(int Year, int Month, decimal Amount, decimal Accumulated, decimal Remaining, decimal Deductible);

    /// <summary>Cât din amortizarea unei luni se deduce: toată, sau cel mult plafonul activului.</summary>
    public static decimal DeductiblePart(PfaAsset asset, decimal amount) =>
        asset.MonthlyDeductionCap is { } cap ? Math.Min(amount, cap) : amount;

    /// <summary>Planul lunilor deschise, în continuarea liniilor blocate (ordonate cronologic).</summary>
    public static List<PlannedLine> Plan(PfaAsset asset, DepreciationStart start, IReadOnlyList<DepreciationLine> locked)
    {
        List<PlannedLine> plan = [];
        if (asset.Kind != AssetKind.FixedAsset || !asset.IsComplete || asset.NormalLifeMonths is not { } life || asset.InServiceDate is not { } inService)
        {
            return plan;
        }

        var first = new DateOnly(inService.Year, inService.Month, 1);
        if (start == DepreciationStart.NextMonth)
        {
            first = first.AddMonths(1);
        }

        decimal accumulated = 0;
        int done = 0;
        if (locked.Count > 0)
        {
            DepreciationLine last = locked[^1];
            accumulated = last.Accumulated;
            done = locked.Count;
            first = new DateOnly(last.Year, last.Month, 1).AddMonths(1);
        }

        int months = life - done;
        decimal left = asset.EntryValue - accumulated;
        if (months <= 0 || left <= 0)
        {
            return plan;
        }

        // Rata din valoarea de intrare; după o recalculare cu luni blocate, din ce a rămas.
        decimal monthly = LedgerInvariants.Round((done == 0 ? asset.EntryValue : left) / (done == 0 ? life : months));
        DateOnly? stop = asset.DisposalDate is { } disposal ? new DateOnly(disposal.Year, disposal.Month, 1) : null;
        for (int index = 0; index < months; index++)
        {
            DateOnly month = first.AddMonths(index);
            if (month > stop)
            {
                break;
            }

            decimal amount = index == months - 1 ? left - monthly * (months - 1) : monthly;
            accumulated += amount;
            plan.Add(new PlannedLine(month.Year, month.Month, amount, accumulated, asset.EntryValue - accumulated, DeductiblePart(asset, amount)));
        }

        return plan;
    }

    /// <summary>
    /// Refă liniile deschise ale activului. Liniile care cad în luni deja închise se blochează pe loc;
    /// o linie într-un an închis e refuzată.
    /// </summary>
    public static async Task<Result> RebuildAsync(IApplicationDbContext db, PfaAsset asset, CancellationToken cancellationToken)
    {
        List<DepreciationLine> existing = await db.DepreciationLines
            .Where(l => l.AssetId == asset.Id)
            .ToListAsync(cancellationToken);
        List<DepreciationLine> locked = [.. existing.Where(l => l.IsLocked).OrderBy(l => l.Year).ThenBy(l => l.Month)];
        if (asset.DisposalDate is { } disposal && locked.Any(l => new DateOnly(l.Year, l.Month, 1) > new DateOnly(disposal.Year, disposal.Month, 1)))
        {
            return Result.Failure(AssetErrors.DisposalBeforeLockedMonths);
        }

        FixedAssetRule? rule = await db.FixedAssetRules.AsNoTracking()
            .Where(r => r.ValidFrom <= (asset.InServiceDate ?? asset.EntryDate))
            .OrderByDescending(r => r.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);
        List<PlannedLine> plan = Plan(asset, rule?.DepreciationStart ?? DepreciationStart.NextMonth, locked);

        HashSet<string> closedPeriods = [.. await db.PfaAccountingPeriods.AsNoTracking()
            .Where(p => p.PfaRegistrationId == asset.PfaRegistrationId && p.Status == AccountingPeriodStatus.Closed)
            .Select(p => p.Period)
            .ToListAsync(cancellationToken)];
        HashSet<int> closedYears = [.. await db.AccountingYears.AsNoTracking()
            .Where(y => y.PfaRegistrationId == asset.PfaRegistrationId && y.Status == AccountingPeriodStatus.Closed)
            .Select(y => y.Year)
            .ToListAsync(cancellationToken)];
        List<DepreciationLine> open = [.. existing.Where(l => !l.IsLocked)];
        if (plan.Any(l => closedYears.Contains(l.Year)) || open.Any(l => closedYears.Contains(l.Year)))
        {
            return Result.Failure(YearClosed);
        }

        db.DepreciationLines.RemoveRange(open);
        foreach (PlannedLine line in plan)
        {
            db.DepreciationLines.Add(new DepreciationLine
            {
                Id = Guid.NewGuid(),
                AssetId = asset.Id,
                PfaRegistrationId = asset.PfaRegistrationId,
                Year = line.Year,
                Month = line.Month,
                Amount = line.Amount,
                DeductibleAmount = line.Deductible,
                Accumulated = line.Accumulated,
                Remaining = line.Remaining,
                IsLocked = closedPeriods.Contains($"{line.Year:0000}-{line.Month:00}"),
            });
        }

        return Result.Success();
    }

    /// <summary>Amortizarea cumulată până la <paramref name="date"/> inclusiv (luna ei intră).</summary>
    public static decimal AccumulatedAt(IEnumerable<DepreciationLine> lines, DateOnly date) =>
        lines.Where(l => l.Year < date.Year || l.Year == date.Year && l.Month <= date.Month).Sum(l => l.Amount);
}
