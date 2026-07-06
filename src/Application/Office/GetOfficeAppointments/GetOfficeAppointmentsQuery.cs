using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Office;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Office.GetOfficeAppointments;

/// <summary>Admin list of office appointments, optionally limited to a date range.</summary>
public sealed record GetOfficeAppointmentsQuery(DateOnly? From = null, DateOnly? To = null)
    : IQuery<List<OfficeAppointmentDto>>;

public sealed record OfficeAppointmentDto(
    Guid Id,
    string Date,
    string Time,
    int DurationMinutes,
    string FullName,
    string Email,
    string Phone,
    string Reason,
    string Status,
    DateTime CreatedAtUtc);

internal sealed class GetOfficeAppointmentsQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetOfficeAppointmentsQuery, List<OfficeAppointmentDto>>
{
    public async Task<Result<List<OfficeAppointmentDto>>> Handle(
        GetOfficeAppointmentsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<OfficeAppointment> queryable = context.OfficeAppointments.AsNoTracking();

        if (query.From is { } from)
        {
            queryable = queryable.Where(a => a.Date >= from);
        }

        if (query.To is { } to)
        {
            queryable = queryable.Where(a => a.Date <= to);
        }

        List<OfficeAppointment> rows = await queryable
            .OrderBy(a => a.Date)
            .ThenBy(a => a.StartTime)
            .ToListAsync(cancellationToken);

        return rows
            .Select(a => new OfficeAppointmentDto(
                a.Id,
                OfficeCalendar.Format(a.Date),
                OfficeCalendar.Format(a.StartTime),
                a.DurationMinutes,
                a.FullName,
                a.Email,
                a.Phone,
                a.Reason,
                a.Status.ToString(),
                a.CreatedAtUtc))
            .ToList();
    }
}
