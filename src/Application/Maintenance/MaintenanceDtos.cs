namespace Application.Maintenance;

/// <param name="CarLabel">„Tesla Model 3, 2021" — lista se citește pe flotă, nu pe id-uri.</param>
public sealed record MaintenanceEntryDto(
    Guid Id,
    Guid CarId,
    string CarLabel,
    string Title,
    string? Notes,
    DateTime PerformedAtUtc,
    int? Mileage,
    long CostBani,
    DateTime? ReminderDateUtc,
    int? ReminderMileage);

/// <summary>Cifrele de sus ale paginii, calculate pe server ca să nu depindă de ce s-a paginat.</summary>
public sealed record MaintenanceSummaryDto(
    long CostLast30DaysBani,
    int ScheduledCount,
    int ActiveReminders,
    int MonitoredCars);

public sealed record MaintenanceOverviewDto(
    MaintenanceSummaryDto Summary,
    List<MaintenanceEntryDto> Entries);
