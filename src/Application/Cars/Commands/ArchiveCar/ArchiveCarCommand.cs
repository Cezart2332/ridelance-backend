using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars.Commands.ToggleCarActive;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.ArchiveCar;

/// <summary>
/// Scoate mașina din flotă fără să șteargă nimic.
/// </summary>
/// <remarks>
/// Ține locul ștergerii pentru proprietari. O mașină ștearsă lua cu ea închirierile, dosarul și
/// mentenanța — adică exact istoricul care trebuie să rămână citibil după ce mașina a plecat.
/// Ștergerea propriu-zisă rămâne, dar doar pentru administrare.
/// </remarks>
public sealed record ArchiveCarCommand(Guid CarId) : ICommand<CarListingStateDto>;

internal sealed class ArchiveCarCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<ArchiveCarCommand, CarListingStateDto>
{
    public async Task<Result<CarListingStateDto>> Handle(
        ArchiveCarCommand command,
        CancellationToken cancellationToken)
    {
        Result<User> userResult = await CarAccessHelper.GetCurrentUserAsync(context, userContext, cancellationToken);
        if (userResult.IsFailure)
        {
            return Result.Failure<CarListingStateDto>(userResult.Error);
        }

        Car? car = await context.Cars.FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);
        if (car is null)
        {
            return Result.Failure<CarListingStateDto>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        Result access = CarAccessHelper.ValidateCarManagement(userResult.Value, car);
        if (access.IsFailure)
        {
            return Result.Failure<CarListingStateDto>(access.Error);
        }

        car.ListingStatus = ListingStatus.Archived;
        car.Status = CarStatus.Archived;
        car.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return new CarListingStateDto(car.ListingStatus.ToString(), car.Active);
    }
}
