using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Office;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Office.CreateOfficeAppointment;

public sealed record CreateOfficeAppointmentCommand(
    string Date,
    string Time,
    string FullName,
    string Email,
    string Phone,
    string Reason,
    Guid? UserId) : ICommand<Guid>;

internal sealed class CreateOfficeAppointmentCommandValidator : AbstractValidator<CreateOfficeAppointmentCommand>
{
    public CreateOfficeAppointmentCommandValidator()
    {
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(200).WithMessage("Numele este obligatoriu.");
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(320).WithMessage("Adresa de email este invalidă.");
        RuleFor(c => c.Phone).NotEmpty().MaximumLength(32).WithMessage("Numărul de telefon este obligatoriu.");
        RuleFor(c => c.Reason).MaximumLength(2000);
        RuleFor(c => c.Date).NotEmpty();
        RuleFor(c => c.Time).NotEmpty();
    }
}

internal sealed class CreateOfficeAppointmentCommandHandler(IApplicationDbContext context)
    : ICommandHandler<CreateOfficeAppointmentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateOfficeAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        if (!OfficeCalendar.TryParseDate(command.Date, out DateOnly date)
            || !OfficeCalendar.TryParseTime(command.Time, out TimeOnly time))
        {
            return Result.Failure<Guid>(Error.Problem("Office.InvalidSlot", "Data sau ora aleasă este invalidă."));
        }

        DateTime nowRo = OfficeCalendar.NowRo();
        var today = DateOnly.FromDateTime(nowRo);
        if (date < today || date == today && time <= TimeOnly.FromDateTime(nowRo))
        {
            return Result.Failure<Guid>(Error.Problem("Office.SlotInPast", "Nu poți face o programare în trecut."));
        }

        Dictionary<DayOfWeek, (bool IsOpen, TimeOnly Open, TimeOnly Close)> schedule =
            await OfficeCalendar.LoadScheduleAsync(context, cancellationToken);
        (bool isOpen, TimeOnly open, TimeOnly close) = schedule[date.DayOfWeek];

        if (!isOpen || time < open || time >= close || !OfficeCalendar.SlotsBetween(open, close).Contains(time))
        {
            return Result.Failure<Guid>(Error.Problem("Office.OutsideSchedule", "Ora aleasă este în afara programului biroului."));
        }

        bool blocked = await context.OfficeBlockedSlots
            .AnyAsync(b => b.Date == date && (b.StartTime == null || b.StartTime == time), cancellationToken);
        if (blocked)
        {
            return Result.Failure<Guid>(Error.Conflict("Office.SlotBlocked", "Intervalul ales nu mai este disponibil."));
        }

        bool taken = await context.OfficeAppointments
            .AnyAsync(
                a => a.Date == date && a.StartTime == time && a.Status == OfficeAppointmentStatus.Confirmed,
                cancellationToken);
        if (taken)
        {
            return Result.Failure<Guid>(Error.Conflict("Office.SlotTaken", "Intervalul ales tocmai a fost rezervat. Alege altă oră."));
        }

        var appointment = new OfficeAppointment
        {
            Id = Guid.NewGuid(),
            Date = date,
            StartTime = time,
            DurationMinutes = OfficeCalendar.SlotMinutes,
            FullName = command.FullName.Trim(),
            Email = command.Email.Trim(),
            Phone = command.Phone.Trim(),
            Reason = command.Reason.Trim(),
            UserId = command.UserId,
            Status = OfficeAppointmentStatus.Confirmed,
            CreatedAtUtc = DateTime.UtcNow,
        };

        context.OfficeAppointments.Add(appointment);
        await context.SaveChangesAsync(cancellationToken);

        return appointment.Id;
    }
}
