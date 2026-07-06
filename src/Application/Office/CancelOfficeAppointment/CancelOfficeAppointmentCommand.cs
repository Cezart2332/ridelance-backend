using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Office;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Office.CancelOfficeAppointment;

public sealed record CancelOfficeAppointmentCommand(Guid AppointmentId) : ICommand;

internal sealed class CancelOfficeAppointmentCommandHandler(IApplicationDbContext context)
    : ICommandHandler<CancelOfficeAppointmentCommand>
{
    public async Task<Result> Handle(CancelOfficeAppointmentCommand command, CancellationToken cancellationToken)
    {
        OfficeAppointment? appointment = await context.OfficeAppointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, cancellationToken);

        if (appointment is null)
        {
            return Result.Failure(Error.NotFound("Office.AppointmentNotFound", "Programarea nu a fost găsită."));
        }

        appointment.Status = OfficeAppointmentStatus.Cancelled;
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
