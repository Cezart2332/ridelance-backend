using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.FiscalProfiles;
using Domain.Expenses;
using Domain.FiscalEstimates;
using Domain.FiscalProfiles;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.FiscalEstimates;

/// <summary>O lună din perioada de dinainte de RIDElance, cum o vede contabilul.</summary>
/// <param name="Income">Venitul trecut de contabil; <c>null</c> = luna nu e completată.</param>
/// <param name="PlatformIncome">Ce avem deja în RIDElance pentru lună (platforme), ca reper.</param>
/// <param name="JoinMonth">Luna în care PFA-ul a intrat în RIDElance: o acoperim doar parțial.</param>
public sealed record PriorPeriodMonthResponse(
    int Month,
    decimal? Income,
    decimal? Expenses,
    decimal PlatformIncome,
    decimal PlatformExpenses,
    bool JoinMonth,
    DateTime? UpdatedAtUtc);

/// <param name="RequiredFrom">De când trebuie acoperit anul (1 ianuarie sau înființarea PFA).</param>
/// <param name="JoinedOn">Ziua intrării în RIDElance; <c>null</c> = necunoscută.</param>
/// <param name="Months">Lunile care se pot completa; goală când PFA-ul e în RIDElance de la începutul anului.</param>
public sealed record PriorPeriodResponse(
    int Year,
    DateOnly RequiredFrom,
    DateOnly? JoinedOn,
    IReadOnlyList<PriorPeriodMonthResponse> Months);

public sealed record PriorPeriodMonthInput(int Month, decimal? Income, decimal? Expenses);

public sealed record GetPriorPeriodQuery(FiscalProfileScope Scope, Guid PfaRegistrationId, int Year) : IQuery<PriorPeriodResponse>;

/// <summary>
/// Contabilul (sau adminul) trece venitul și cheltuielile lunilor de dinainte de RIDElance. O lună
/// cu ambele sume goale se șterge. Taxele estimate ale anului se recalculează.
/// </summary>
public sealed record SavePriorPeriodCommand(
    FiscalProfileScope Scope,
    Guid PfaRegistrationId,
    int Year,
    IReadOnlyList<PriorPeriodMonthInput> Months) : ICommand<PriorPeriodResponse>;

internal static class PriorPeriod
{
    public const decimal MaxAmount = 10_000_000;

    public static readonly Error StaffOnly =
        Error.Problem("PriorPeriod.StaffOnly", "Doar contabilul sau adminul completează perioada de dinainte de RIDElance.");

    public static readonly Error InvalidMonth =
        Error.Problem("PriorPeriod.InvalidMonth", "Luna nu face parte din perioada de dinainte de RIDElance.");

    public static readonly Error InvalidAmount =
        Error.Problem("PriorPeriod.InvalidAmount", "Sumele trebuie să fie între 0 și 10.000.000 lei.");

    /// <summary>
    /// Lunile de completat: de la luna în care începe anul fiscal al PFA-ului până la luna intrării
    /// în RIDElance, inclusiv (e acoperită doar de la data intrării). Nimic după luna curentă.
    /// </summary>
    public static IReadOnlyList<int> EditableMonths(int year, DateOnly requiredFrom, DateOnly? joinedOn, DateOnly today)
    {
        int last = joinedOn switch
        {
            DateOnly j when j.Year < year => 0,
            DateOnly j when j.Year == year => j.Month,
            _ => 12,
        };

        if (year == today.Year)
        {
            last = Math.Min(last, today.Month);
        }
        else if (year > today.Year)
        {
            last = 0;
        }

        int first = requiredFrom.Year == year ? requiredFrom.Month : 1;
        return first > last ? [] : Enumerable.Range(first, last - first + 1).ToList();
    }

    public static async Task<PriorPeriodResponse> BuildAsync(
        IApplicationDbContext context,
        PfaRegistration pfa,
        PfaTaxProfile profile,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        int year = profile.TaxYear;
        DateOnly? joinedOn = FinancialSnapshotProvider.AccessOn(pfa, profile);
        DateOnly requiredFrom = FinancialSnapshotProvider.RequiredFrom(pfa, profile, joinedOn);
        IReadOnlyList<int> editable = EditableMonths(year, requiredFrom, joinedOn, today);

        if (editable.Count == 0)
        {
            return new PriorPeriodResponse(year, requiredFrom, joinedOn, []);
        }

        Dictionary<int, PfaPriorPeriodMonth> saved = await context.PfaPriorPeriodMonths
            .AsNoTracking()
            .Where(m => m.PfaRegistrationId == pfa.Id && m.Year == year)
            .ToDictionaryAsync(m => m.Month, cancellationToken);

        List<PfaMonthlyIncome> incomes = await context.PfaMonthlyIncomes
            .AsNoTracking()
            .Where(i => i.PfaRegistrationId == pfa.Id && i.Year == year && editable.Contains(i.Month))
            .ToListAsync(cancellationToken);

        var expenses = await context.DeductibleExpenses
            .AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfa.Id && e.Year == year && e.Status == ExpenseStatus.Confirmed && editable.Contains(e.Month))
            .GroupBy(e => e.Month)
            .Select(g => new { Month = g.Key, Amount = g.Sum(e => e.AmountRon ?? 0m) })
            .ToListAsync(cancellationToken);

        var months = editable
            .Select(month =>
            {
                saved.TryGetValue(month, out PfaPriorPeriodMonth? entry);
                return new PriorPeriodMonthResponse(
                    month,
                    entry?.Income,
                    entry?.Expenses,
                    incomes.Where(i => i.Month == month).Sum(i => i.ComputeVenitTotal()),
                    expenses.Where(e => e.Month == month).Sum(e => e.Amount),
                    joinedOn is DateOnly j && j.Year == year && j.Month == month,
                    entry?.UpdatedAtUtc);
            })
            .ToList();

        return new PriorPeriodResponse(year, requiredFrom, joinedOn, months);
    }
}

internal sealed class GetPriorPeriodQueryHandler(IApplicationDbContext context, FiscalProfileService profiles)
    : IQueryHandler<GetPriorPeriodQuery, PriorPeriodResponse>
{
    public async Task<Result<PriorPeriodResponse>> Handle(GetPriorPeriodQuery query, CancellationToken cancellationToken)
    {
        if (!FiscalProfileService.IsValidYear(query.Year))
        {
            return Result.Failure<PriorPeriodResponse>(FiscalProfileService.InvalidYear);
        }

        if (query.Scope == FiscalProfileScope.Pfa)
        {
            return Result.Failure<PriorPeriodResponse>(PriorPeriod.StaffOnly);
        }

        Result<PfaRegistration> pfa = await profiles.ResolveAsync(query.Scope, query.PfaRegistrationId, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<PriorPeriodResponse>(pfa.Error);
        }

        PfaTaxProfile profile = await profiles.GetOrCreateAsync(pfa.Value, query.Year, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await PriorPeriod.BuildAsync(context, pfa.Value, profile, Today(profiles), cancellationToken);
    }

    internal static DateOnly Today(FiscalProfileService profiles) =>
        DateOnly.FromDateTime(FiscalProfileService.ToRomania(profiles.UtcNow));
}

internal sealed class SavePriorPeriodCommandHandler(IApplicationDbContext context, FiscalProfileService profiles)
    : ICommandHandler<SavePriorPeriodCommand, PriorPeriodResponse>
{
    public async Task<Result<PriorPeriodResponse>> Handle(SavePriorPeriodCommand command, CancellationToken cancellationToken)
    {
        if (!FiscalProfileService.IsValidYear(command.Year))
        {
            return Result.Failure<PriorPeriodResponse>(FiscalProfileService.InvalidYear);
        }

        if (command.Scope == FiscalProfileScope.Pfa)
        {
            return Result.Failure<PriorPeriodResponse>(PriorPeriod.StaffOnly);
        }

        if (command.Months.Any(m => m.Income is < 0 or > PriorPeriod.MaxAmount || m.Expenses is < 0 or > PriorPeriod.MaxAmount))
        {
            return Result.Failure<PriorPeriodResponse>(PriorPeriod.InvalidAmount);
        }

        Result<PfaRegistration> resolved = await profiles.ResolveAsync(command.Scope, command.PfaRegistrationId, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<PriorPeriodResponse>(resolved.Error);
        }

        PfaRegistration pfa = resolved.Value;
        PfaTaxProfile profile = await profiles.GetOrCreateAsync(pfa, command.Year, cancellationToken);
        DateOnly today = GetPriorPeriodQueryHandler.Today(profiles);
        DateOnly? joinedOn = FinancialSnapshotProvider.AccessOn(pfa, profile);
        IReadOnlyList<int> editable = PriorPeriod.EditableMonths(
            command.Year, FinancialSnapshotProvider.RequiredFrom(pfa, profile, joinedOn), joinedOn, today);

        if (command.Months.Any(m => !editable.Contains(m.Month)) || command.Months.DistinctBy(m => m.Month).Count() != command.Months.Count)
        {
            return Result.Failure<PriorPeriodResponse>(PriorPeriod.InvalidMonth);
        }

        List<PfaPriorPeriodMonth> existing = await context.PfaPriorPeriodMonths
            .Where(m => m.PfaRegistrationId == pfa.Id && m.Year == command.Year)
            .ToListAsync(cancellationToken);

        DateTime now = profiles.UtcNow;
        int changed = 0;
        foreach (PriorPeriodMonthInput input in command.Months)
        {
            PfaPriorPeriodMonth? entry = existing.FirstOrDefault(m => m.Month == input.Month);
            if (input.Income is null && input.Expenses is null)
            {
                if (entry is not null)
                {
                    context.PfaPriorPeriodMonths.Remove(entry);
                    changed++;
                }

                continue;
            }

            decimal income = Math.Round(input.Income ?? 0, 2);
            decimal expenses = Math.Round(input.Expenses ?? 0, 2);
            if (entry is null)
            {
                entry = new PfaPriorPeriodMonth
                {
                    Id = Guid.NewGuid(),
                    PfaRegistrationId = pfa.Id,
                    Year = command.Year,
                    Month = input.Month,
                };
                context.PfaPriorPeriodMonths.Add(entry);
            }
            else if (entry.Income == income && entry.Expenses == expenses)
            {
                continue;
            }

            entry.Income = income;
            entry.Expenses = expenses;
            entry.UpdatedAtUtc = now;
            entry.UpdatedByUserId = profiles.CallerId;
            changed++;
        }

        if (changed > 0)
        {
            context.PfaActivityLogs.Add(new PfaActivityLog
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = pfa.Id,
                ActivityType = "PriorPeriodUpdated",
                Description = $"Veniturile și cheltuielile de dinainte de RIDElance ({command.Year}) au fost actualizate: {(changed == 1 ? "o lună" : $"{changed} luni")}.",
                CreatedAtUtc = now,
                PerformedByUserId = profiles.CallerId,
            });
            await FiscalEstimateInvalidation.MarkStaleAsync(context, pfa.Id, command.Year, now, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        return await PriorPeriod.BuildAsync(context, pfa, profile, today, cancellationToken);
    }
}
