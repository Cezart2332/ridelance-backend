using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.ApproveCarListing;

public sealed record ApproveCarListingCommand(Guid CarId, bool Approve) : ICommand;

internal sealed class ApproveCarListingCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<ApproveCarListingCommand>
{
    public async Task<Result> Handle(ApproveCarListingCommand command, CancellationToken cancellationToken)
    {
        Result<User> userResult = await CarAccessHelper.GetCurrentUserAsync(context, userContext, cancellationToken);
        if (userResult.IsFailure)
        {
            return Result.Failure(userResult.Error);
        }

        if (userResult.Value.Role != UserRole.Admin)
        {
            return Result.Failure(Error.Problem("Car.Forbidden", "Doar administratorii pot valida anunțurile."));
        }

        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        if (command.Approve)
        {
            if (car.PaymentStatus != CarListingPaymentStatus.NotRequired &&
                car.PaymentStatus != CarListingPaymentStatus.Paid)
            {
                return Result.Failure(Error.Problem(
                    "Car.PaymentRequired",
                    "Anunțul trebuie plătit înainte de aprobare."));
            }

            car.ApprovalStatus = CarApprovalStatus.Approved;
            car.ListingStatus = ListingStatus.Published;
        }
        else
        {
            car.ApprovalStatus = CarApprovalStatus.Rejected;
            // Draft, nu Archived: respins înseamnă „de refăcut", nu „scos din flotă".
            car.ListingStatus = ListingStatus.Draft;
        }

        car.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
