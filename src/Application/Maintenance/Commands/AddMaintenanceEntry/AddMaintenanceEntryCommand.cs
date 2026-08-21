using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Maintenance.Commands.AddMaintenanceEntry;

public sealed record AddMaintenanceEntryCommand(
    Guid CarId,
    string Title,
    string? Notes,
    DateTime PerformedAtUtc,
    int? Mileage,
    long CostBani,
    DateTime? ReminderDateUtc,
    int? ReminderMileage) : ICommand<Guid>;

internal sealed class AddMaintenanceEntryCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<AddMaintenanceEntryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddMaintenanceEntryCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
        {
            return Result.Failure<Guid>(
                Error.Problem("Maintenance.TitleRequired", "Descrierea intervenției este obligatorie."));
        }

        if (command.CostBani < 0)
        {
            return Result.Failure<Guid>(
                Error.Problem("Maintenance.NegativeCost", "Costul nu poate fi negativ."));
        }

        // Verificarea de proprietate se face pe mașină, nu pe intervenție: altfel oricine ar fi
        // putut atașa istoric de service mașinii altcuiva.
        Car? car = await context.Cars
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        if (car.PostedByUserId != userContext.UserId)
        {
            return Result.Failure<Guid>(
                Error.Problem("Maintenance.Forbidden", "Mașina nu îți aparține."));
        }

        var entry = new MaintenanceEntry
        {
            Id = Guid.NewGuid(),
            CarId = car.Id,
            OwnerUserId = userContext.UserId,
            Title = command.Title.Trim(),
            Notes = command.Notes?.Trim(),
            PerformedAtUtc = command.PerformedAtUtc,
            Mileage = command.Mileage,
            CostBani = command.CostBani,
            ReminderDateUtc = command.ReminderDateUtc,
            ReminderMileage = command.ReminderMileage,
        };

        context.MaintenanceEntries.Add(entry);
        await context.SaveChangesAsync(cancellationToken);

        return entry.Id;
    }
}
