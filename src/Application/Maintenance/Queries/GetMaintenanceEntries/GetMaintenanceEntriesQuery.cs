using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Maintenance.Queries.GetMaintenanceEntries;

/// <param name="CarId">Filtrare pe o singură mașină. `null` = toată flota.</param>
public sealed record GetMaintenanceEntriesQuery(Guid? CarId = null) : IQuery<MaintenanceOverviewDto>;

internal sealed class GetMaintenanceEntriesQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetMaintenanceEntriesQuery, MaintenanceOverviewDto>
{
    public async Task<Result<MaintenanceOverviewDto>> Handle(
        GetMaintenanceEntriesQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<MaintenanceEntry> queryable = context.MaintenanceEntries
            .AsNoTracking()
            .Where(m => m.OwnerUserId == userContext.UserId);

        if (query.CarId.HasValue)
        {
            queryable = queryable.Where(m => m.CarId == query.CarId.Value);
        }

        List<MaintenanceEntry> entries = await queryable
            .OrderByDescending(m => m.PerformedAtUtc)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        // Etichetele mașinilor, într-un singur query: un nume per intervenție ar fi fost N cereri.
        var carIds = entries.Select(m => m.CarId).Distinct().ToList();
        Dictionary<Guid, string> labels = carIds.Count == 0
            ? []
            : await context.Cars
                .AsNoTracking()
                .Where(c => carIds.Contains(c.Id))
                .Select(c => new { c.Id, Label = c.Brand + " " + c.Model + ", " + c.Year })
                .ToDictionaryAsync(x => x.Id, x => x.Label, cancellationToken);

        DateTime now = DateTime.UtcNow;
        DateTime last30 = now.AddDays(-30);

        int monitoredCars = await context.Cars
            .AsNoTracking()
            .CountAsync(c => c.PostedByUserId == userContext.UserId, cancellationToken);

        var summary = new MaintenanceSummaryDto(
            // Doar intervențiile deja efectuate contează drept cost; o programare nu s-a plătit încă.
            entries.Where(m => m.PerformedAtUtc >= last30 && m.PerformedAtUtc <= now).Sum(m => m.CostBani),
            entries.Count(m => m.PerformedAtUtc > now),
            entries.Count(m => m.ReminderDateUtc.HasValue && m.ReminderDateUtc.Value > now
                || m.ReminderMileage.HasValue),
            monitoredCars);

        var dtos = entries
            .Select(m => new MaintenanceEntryDto(
                m.Id,
                m.CarId,
                labels.GetValueOrDefault(m.CarId, "Mașină ștearsă"),
                m.Title,
                m.Notes,
                m.PerformedAtUtc,
                m.Mileage,
                m.CostBani,
                m.ReminderDateUtc,
                m.ReminderMileage))
            .ToList();

        return Result.Success(new MaintenanceOverviewDto(summary, dtos));
    }
}
