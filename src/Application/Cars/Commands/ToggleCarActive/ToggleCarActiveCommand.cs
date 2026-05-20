using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.ToggleCarActive;

public sealed record ToggleCarActiveCommand(Guid CarId) : ICommand<bool>;

internal sealed class ToggleCarActiveCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<ToggleCarActiveCommand, bool>
{
    public async Task<Result<bool>> Handle(ToggleCarActiveCommand command, CancellationToken cancellationToken)
    {
        Result<User> userResult = await CarAccessHelper.GetCurrentUserAsync(context, userContext, cancellationToken);
        if (userResult.IsFailure)
        {
            return Result.Failure<bool>(userResult.Error);
        }

        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<bool>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        Result access = CarAccessHelper.ValidateCarManagement(userResult.Value, car);
        if (access.IsFailure)
        {
            return Result.Failure<bool>(access.Error);
        }

        if (userResult.Value.Role == UserRole.CarPoster && car.ApprovalStatus != CarApprovalStatus.Approved)
        {
            return Result.Failure<bool>(Error.Problem(
                "Car.NotApproved",
                "Anunțul trebuie aprobat de administrator înainte de a fi activat."));
        }

        car.Active = !car.Active;
        car.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return car.Active;
    }
}
