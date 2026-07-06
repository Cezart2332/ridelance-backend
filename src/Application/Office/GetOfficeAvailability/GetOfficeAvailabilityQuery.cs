using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Office;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Office.GetOfficeAvailability;

/// <summary>Month overview for the booking calendar: which days are open, full or closed.</summary>
public sealed record GetOfficeAvailabilityQuery(int Year, int Month) : IQuery<OfficeMonthAvailabilityResponse>;

public sealed record OfficeDayAvailabilityDto(
    string Date,
    // "closed" | "past" | "full" | "available"
    string Status,
    int FreeSlots);

public sealed record OfficeMonthAvailabilityResponse(
    int Year,
    int Month,
    List<OfficeDayAvailabilityDto> Days);

internal sealed class GetOfficeAvailabilityQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetOfficeAvailabilityQuery, OfficeMonthAvailabilityResponse>
{
    public async Task<Result<OfficeMonthAvailabilityResponse>> Handle(
        GetOfficeAvailabilityQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Month is < 1 or > 12 || query.Year is < 2020 or > 2100)
        {
            return Result.Failure<OfficeMonthAvailabilityResponse>(
                Error.Problem("Office.InvalidPeriod", "Perioada cerută este invalidă."));
        }

        var monthStart = new DateOnly(query.Year, query.Month, 1);
        DateOnly monthEnd = monthStart.AddMonths(1);

        Dictionary<DayOfWeek, (bool IsOpen, TimeOnly Open, TimeOnly Close)> schedule =
            await OfficeCalendar.LoadScheduleAsync(context, cancellationToken);

        List<OfficeAppointment> appointments = await context.OfficeAppointments
            .AsNoTracking()
            .Where(a => a.Date >= monthStart && a.Date < monthEnd && a.Status == OfficeAppointmentStatus.Confirmed)
            .ToListAsync(cancellationToken);

        List<OfficeBlockedSlot> blocks = await context.OfficeBlockedSlots
            .AsNoTracking()
            .Where(b => b.Date >= monthStart && b.Date < monthEnd)
            .ToListAsync(cancellationToken);

        DateTime nowRo = OfficeCalendar.NowRo();
        var today = DateOnly.FromDateTime(nowRo);
        var nowTime = TimeOnly.FromDateTime(nowRo);

        var days = new List<OfficeDayAvailabilityDto>();
        for (DateOnly date = monthStart; date < monthEnd; date = date.AddDays(1))
        {
            (bool isOpen, TimeOnly open, TimeOnly close) = schedule[date.DayOfWeek];
            bool wholeDayBlocked = blocks.Any(b => b.Date == date && b.StartTime is null);

            if (date < today)
            {
                days.Add(new OfficeDayAvailabilityDto(OfficeCalendar.Format(date), "past", 0));
                continue;
            }

            if (!isOpen || wholeDayBlocked)
            {
                days.Add(new OfficeDayAvailabilityDto(OfficeCalendar.Format(date), "closed", 0));
                continue;
            }

            int freeSlots = OfficeCalendar.SlotsBetween(open, close).Count(slot =>
                !(date == today && slot <= nowTime)
                && !appointments.Any(a => a.Date == date && a.StartTime == slot)
                && !blocks.Any(b => b.Date == date && b.StartTime == slot));

            days.Add(new OfficeDayAvailabilityDto(
                OfficeCalendar.Format(date),
                freeSlots > 0 ? "available" : "full",
                freeSlots));
        }

        return new OfficeMonthAvailabilityResponse(query.Year, query.Month, days);
    }
}
