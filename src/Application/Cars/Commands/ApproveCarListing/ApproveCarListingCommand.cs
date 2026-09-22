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
            car.ApprovalStatus = CarApprovalStatus.Approved;

            // Aprobarea publică anunțul doar dacă flota mai are loc în abonament. Altfel rămâne
            // aprobat, dar nepublicat: proprietarul îl publică singur după ce retrage altul.
            bool hasRoom = true;
            if (car.PostedByUserId is Guid ownerId)
            {
                bool ownerIsFleet = await context.Users
                    .AnyAsync(u => u.Id == ownerId && u.Role == UserRole.CarPoster, cancellationToken);
                if (ownerIsFleet)
                {
                    int used = await ListingQuota.CountUsedAsync(context, ownerId, car.Id, cancellationToken);
                    hasRoom = used < ListingAllowance.IncludedInFleetPlan || car.PaymentStatus == CarListingPaymentStatus.Paid;
                }
            }

            if (hasRoom)
            {
                car.ListingStatus = ListingStatus.Published;
            }
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
