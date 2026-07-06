using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Office;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Office.OfficeSchedule;

public sealed record OfficeScheduleDayDto(
    // .NET DayOfWeek value: 0 = Sunday … 6 = Saturday
    int Day,
    bool IsOpen,
    string OpenTime,
    string CloseTime);

/// <summary>Weekly opening-hours template (always all 7 days, defaults filled in).</summary>
public sealed record GetOfficeScheduleQuery : IQuery<List<OfficeScheduleDayDto>>;

internal sealed class GetOfficeScheduleQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetOfficeScheduleQuery, List<OfficeScheduleDayDto>>
{
    public async Task<Result<List<OfficeScheduleDayDto>>> Handle(
        GetOfficeScheduleQuery query,
        CancellationToken cancellationToken)
    {
        Dictionary<DayOfWeek, (bool IsOpen, TimeOnly Open, TimeOnly Close)> schedule =
            await OfficeCalendar.LoadScheduleAsync(context, cancellationToken);

        return schedule
            .OrderBy(kv => ((int)kv.Key + 6) % 7) // Monday first
            .Select(kv => new OfficeScheduleDayDto(
                (int)kv.Key,
                kv.Value.IsOpen,
                OfficeCalendar.Format(kv.Value.Open),
                OfficeCalendar.Format(kv.Value.Close)))
            .ToList();
    }
}

public sealed record UpsertOfficeScheduleCommand(List<OfficeScheduleDayDto> Days) : ICommand;

internal sealed class UpsertOfficeScheduleCommandHandler(IApplicationDbContext context)
    : ICommandHandler<UpsertOfficeScheduleCommand>
{
    public async Task<Result> Handle(UpsertOfficeScheduleCommand command, CancellationToken cancellationToken)
    {
        List<OfficeScheduleDay> existing = await context.OfficeScheduleDays.ToListAsync(cancellationToken);

        foreach (OfficeScheduleDayDto dto in command.Days)
        {
            if (dto.Day is < 0 or > 6
                || !OfficeCalendar.TryParseTime(dto.OpenTime, out TimeOnly open)
                || !OfficeCalendar.TryParseTime(dto.CloseTime, out TimeOnly close))
            {
                return Result.Failure(Error.Problem("Office.InvalidSchedule", "Programul trimis este invalid."));
            }

            if (dto.IsOpen && close <= open)
            {
                return Result.Failure(Error.Problem(
                    "Office.InvalidSchedule",
                    "Ora de închidere trebuie să fie după ora de deschidere."));
            }

            var day = (DayOfWeek)dto.Day;
            OfficeScheduleDay? row = existing.Find(r => r.Day == day);
            if (row is null)
            {
                context.OfficeScheduleDays.Add(new OfficeScheduleDay
                {
                    Id = Guid.NewGuid(),
                    Day = day,
                    IsOpen = dto.IsOpen,
                    OpenTime = open,
                    CloseTime = close,
                });
            }
            else
            {
                row.IsOpen = dto.IsOpen;
                row.OpenTime = open;
                row.CloseTime = close;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
