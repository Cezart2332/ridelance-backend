using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Office;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Office.GetOfficeDaySlots;

/// <summary>
/// Slot list for one day. Public callers get availability only; admin callers
/// (IncludeDetails) also see who booked each slot and which blocks exist.
/// </summary>
public sealed record GetOfficeDaySlotsQuery(DateOnly Date, bool IncludeDetails = false)
    : IQuery<OfficeDaySlotsResponse>;

public sealed record OfficeSlotDto(
    string Time,
    // "free" | "past" | "booked" | "blocked"
    string Status,
    // Populated only for admin (IncludeDetails):
    Guid? AppointmentId,
    string? VisitorName,
    string? VisitorPhone,
    Guid? BlockedSlotId,
    string? BlockNote);

public sealed record OfficeDaySlotsResponse(
    string Date,
    bool IsOpen,
    bool WholeDayBlocked,
    // Set only for admin calls, so the whole-day block can be lifted.
    Guid? WholeDayBlockId,
    string? OpenTime,
    string? CloseTime,
    List<OfficeSlotDto> Slots);

internal sealed class GetOfficeDaySlotsQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetOfficeDaySlotsQuery, OfficeDaySlotsResponse>
{
    public async Task<Result<OfficeDaySlotsResponse>> Handle(
        GetOfficeDaySlotsQuery query,
        CancellationToken cancellationToken)
    {
        Dictionary<DayOfWeek, (bool IsOpen, TimeOnly Open, TimeOnly Close)> schedule =
            await OfficeCalendar.LoadScheduleAsync(context, cancellationToken);
        (bool isOpen, TimeOnly open, TimeOnly close) = schedule[query.Date.DayOfWeek];

        List<OfficeAppointment> appointments = await context.OfficeAppointments
            .AsNoTracking()
            .Where(a => a.Date == query.Date && a.Status == OfficeAppointmentStatus.Confirmed)
            .ToListAsync(cancellationToken);

        List<OfficeBlockedSlot> blocks = await context.OfficeBlockedSlots
            .AsNoTracking()
            .Where(b => b.Date == query.Date)
            .ToListAsync(cancellationToken);

        bool wholeDayBlocked = blocks.Any(b => b.StartTime is null);

        DateTime nowRo = OfficeCalendar.NowRo();
        var today = DateOnly.FromDateTime(nowRo);
        var nowTime = TimeOnly.FromDateTime(nowRo);

        var slots = new List<OfficeSlotDto>();
        if (isOpen)
        {
            foreach (TimeOnly slot in OfficeCalendar.SlotsBetween(open, close))
            {
                OfficeAppointment? appointment = appointments.Find(a => a.StartTime == slot);
                OfficeBlockedSlot? block = blocks.Find(b => b.StartTime == slot);
                bool isPast = query.Date < today || query.Date == today && slot <= nowTime;

                string status = (booked: appointment is not null, blocked: wholeDayBlocked || block is not null, isPast) switch
                {
                    { booked: true } => "booked",
                    { blocked: true } => "blocked",
                    { isPast: true } => "past",
                    _ => "free",
                };

                slots.Add(query.IncludeDetails
                    ? new OfficeSlotDto(
                        OfficeCalendar.Format(slot),
                        status,
                        appointment?.Id,
                        appointment?.FullName,
                        appointment?.Phone,
                        block?.Id,
                        block?.Note)
                    : new OfficeSlotDto(OfficeCalendar.Format(slot), status, null, null, null, null, null));
            }
        }

        return new OfficeDaySlotsResponse(
            OfficeCalendar.Format(query.Date),
            isOpen,
            wholeDayBlocked,
            query.IncludeDetails ? blocks.Find(b => b.StartTime is null)?.Id : null,
            isOpen ? OfficeCalendar.Format(open) : null,
            isOpen ? OfficeCalendar.Format(close) : null,
            slots);
    }
}
