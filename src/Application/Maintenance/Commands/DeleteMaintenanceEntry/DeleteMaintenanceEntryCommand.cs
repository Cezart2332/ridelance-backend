using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Maintenance.Commands.DeleteMaintenanceEntry;

public sealed record DeleteMaintenanceEntryCommand(Guid Id) : ICommand;

internal sealed class DeleteMaintenanceEntryCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<DeleteMaintenanceEntryCommand>
{
    public async Task<Result> Handle(DeleteMaintenanceEntryCommand command, CancellationToken cancellationToken)
    {
        MaintenanceEntry? entry = await context.MaintenanceEntries
            .FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);

        if (entry is null)
        {
            return Result.Failure(Error.NotFound("Maintenance.NotFound", "Intervenția nu a fost găsită."));
        }

        // Aceeași eroare pentru „nu există" și „nu e a ta" ar fi fost mai discretă, dar aici
        // proprietarul e singurul care poate ajunge la id-uri, deci mesajul clar ajută.
        if (entry.OwnerUserId != userContext.UserId)
        {
            return Result.Failure(Error.Problem("Maintenance.Forbidden", "Intervenția nu îți aparține."));
        }

        context.MaintenanceEntries.Remove(entry);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
