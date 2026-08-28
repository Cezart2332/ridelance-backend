using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Application.Rentals.Checks;
using Domain.Cars;

namespace Application.Rentals.Commands.CloseRental;

/// <summary>Încheie o închiriere. Predarea poate avea loc și înainte de data planificată.</summary>
public sealed record CloseRentalCommand(Guid Id, int? EndMileage) : ICommand;

internal sealed class CloseRentalCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<CloseRentalCommand>
{
    public async Task<Result> Handle(CloseRentalCommand command, CancellationToken cancellationToken)
    {
        Rental? rental = await context.Rentals
            .FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken);

        if (rental is null)
        {
            return Result.Failure(Error.NotFound("Rental.NotFound", "Închirierea nu a fost găsită."));
        }

        if (rental.OwnerUserId != userContext.UserId)
        {
            return Result.Failure(Error.Problem("Rental.Forbidden", "Închirierea nu îți aparține."));
        }

        if (rental.ClosedAtUtc.HasValue)
        {
            return Result.Failure(Error.Problem("Rental.AlreadyClosed", "Închirierea e deja încheiată."));
        }

        // `EndAtUtc` rămâne neatins: e ce s-a convenit, nu ce s-a întâmplat.
        rental.ClosedAtUtc = DateTime.UtcNow;
        rental.UpdatedAtUtc = DateTime.UtcNow;

        if (command.EndMileage.HasValue)
        {
            string note = $"Kilometraj la predare: {command.EndMileage.Value}.";
            rental.Notes = string.IsNullOrWhiteSpace(rental.Notes) ? note : $"{rental.Notes}\n{note}";
        }

        VehicleTimeline.Record(
            context,
            rental.CarId,
            VehicleEventType.RentalClosed,
            $"Închiriere {rental.PublicCode} încheiată",
            rental.Id);

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
