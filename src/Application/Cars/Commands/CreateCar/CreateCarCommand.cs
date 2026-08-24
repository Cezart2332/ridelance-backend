using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars.Scoring;
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
    string ListingSource,
    CarListingDetails? Details = null) : ICommand<Guid>;

internal sealed class CreateCarCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    ListingScoreService scoreService)
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

        CarListingSource listingSource = user.Role == UserRole.Admin
            ? CarListingSource.Ridelance
            : CarListingSource.External;

        bool isAdmin = user.Role == UserRole.Admin;
        bool requiresPayment = user.Role == UserRole.CarPoster;
        var carId = Guid.NewGuid();
        var car = new Car
        {
            Id = carId,
            Brand = command.Brand,
            Model = command.Model,
            Year = command.Year,
            Slug = CarSlug.Generate(command.Brand, command.Model, command.Year, carId),
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

        CarListingDetailsMapper.Apply(car, command.Details);

        context.Cars.Add(car);

        // Scorul se calculează în aceeași tranzacție: un anunț fără scor ar cădea la coada
        // sortării „Recomandate" până la primul job nocturn.
        await scoreService.RecalculateAsync(car, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        return car.Id;
    }
}
