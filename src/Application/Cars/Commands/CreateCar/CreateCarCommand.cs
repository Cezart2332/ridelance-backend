using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.CreateCar;

public sealed record CreateCarCommand(
    string Brand,
    string Model,
    int Year,
    string Engine,
    string Transmission,
    string Location,
    decimal PricePerWeek,
    decimal? OldPrice,
    bool DiscountActive,
    decimal? Garantie,
    string OfferType,
    string Status,
    List<string> UberCategories,
    List<string> BoltCategories,
    List<string> Badges,
    string Description,
    bool Active,
    string ListingSource) : ICommand<Guid>;

internal sealed class CreateCarCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<CreateCarCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCarCommand command, CancellationToken cancellationToken)
    {
        Result<User> userResult = await CarAccessHelper.GetCurrentUserAsync(context, userContext, cancellationToken);
        if (userResult.IsFailure)
        {
            return Result.Failure<Guid>(userResult.Error);
        }

        User user = userResult.Value;
        if (!CarAccessHelper.CanPostCars(user))
        {
            return Result.Failure<Guid>(Error.Problem("Car.Forbidden", "Nu ai permisiunea de a publica mașini."));
        }

        if (!Enum.TryParse<CarOfferType>(command.OfferType, out CarOfferType offerType))
        {
            offerType = CarOfferType.Weekly;
        }

        if (!Enum.TryParse<CarStatus>(command.Status, out CarStatus status))
        {
            status = CarStatus.Available;
        }

        if (!Enum.TryParse<CarListingSource>(command.ListingSource, ignoreCase: true, out CarListingSource listingSource))
        {
            listingSource = CarListingSource.Ridelance;
        }

        bool isAdmin = user.Role == UserRole.Admin;
        bool requiresPayment = user.Role == UserRole.CarPoster;
        var car = new Car
        {
            Id = Guid.NewGuid(),
            Brand = command.Brand,
            Model = command.Model,
            Year = command.Year,
            Engine = command.Engine,
            Transmission = command.Transmission,
            Location = command.Location,
            PricePerWeek = command.PricePerWeek,
            OldPrice = command.OldPrice,
            DiscountActive = command.DiscountActive,
            Garantie = command.Garantie,
            OfferType = offerType,
            Status = status,
            UberCategories = command.UberCategories,
            BoltCategories = command.BoltCategories,
            Badges = command.Badges,
            Description = command.Description,
            PostedByUserId = user.Id,
            ListingSource = listingSource,
            ApprovalStatus = isAdmin ? CarApprovalStatus.Approved : CarApprovalStatus.Pending,
            PaymentStatus = requiresPayment ? CarListingPaymentStatus.Pending : CarListingPaymentStatus.NotRequired,
            Active = isAdmin && command.Active,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        context.Cars.Add(car);
        await context.SaveChangesAsync(cancellationToken);

        return car.Id;
    }
}
