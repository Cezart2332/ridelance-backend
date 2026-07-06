using System.Globalization;
using Application.Abstractions.Data;
using Domain.Office;
using Microsoft.EntityFrameworkCore;

namespace Application.Office;

/// <summary>
/// Shared rules for the office visit calendar: opening hours, 30-minute slots
/// and the current time in office-local (Romania) time.
/// </summary>
public static class OfficeCalendar
{
    public const int SlotMinutes = 30;

    private static readonly TimeZoneInfo RomaniaTz =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");

    public static DateTime NowRo() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, RomaniaTz);

    /// <summary>Default template used until an admin saves a custom schedule.</summary>
    public static (bool IsOpen, TimeOnly Open, TimeOnly Close) DefaultFor(DayOfWeek day) =>
        day is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? (false, new TimeOnly(9, 0), new TimeOnly(17, 0))
            : (true, new TimeOnly(9, 0), new TimeOnly(17, 0));

    /// <summary>Loads the weekly schedule, falling back to the default template per missing day.</summary>
    public static async Task<Dictionary<DayOfWeek, (bool IsOpen, TimeOnly Open, TimeOnly Close)>> LoadScheduleAsync(
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        List<OfficeScheduleDay> rows = await context.OfficeScheduleDays
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var schedule = new Dictionary<DayOfWeek, (bool, TimeOnly, TimeOnly)>();
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            OfficeScheduleDay? row = rows.Find(r => r.Day == day);
            schedule[day] = row is null
                ? DefaultFor(day)
                : (row.IsOpen, row.OpenTime, row.CloseTime);
        }

        return schedule;
    }

    /// <summary>All slot start times for a working window, every 30 minutes.</summary>
    public static List<TimeOnly> SlotsBetween(TimeOnly open, TimeOnly close)
    {
        var slots = new List<TimeOnly>();
        for (TimeOnly t = open; t < close; t = t.AddMinutes(SlotMinutes))
        {
            slots.Add(t);
            if (t.Hour == 23 && t.Minute >= 30)
            {
                break; // guard against midnight wrap-around
            }
        }

        return slots;
    }

    public static string Format(TimeOnly time) =>
        time.ToString("HH\\:mm", CultureInfo.InvariantCulture);

    public static string Format(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH\\:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
