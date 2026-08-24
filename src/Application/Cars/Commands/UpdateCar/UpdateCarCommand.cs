using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars.Scoring;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.UpdateCar;

public sealed record UpdateCarCommand(
    Guid CarId,
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
    CarListingDetails? Details = null) : ICommand;

internal sealed class UpdateCarCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    ListingScoreService scoreService)
    : ICommandHandler<UpdateCarCommand>
{
    public async Task<Result> Handle(UpdateCarCommand command, CancellationToken cancellationToken)
    {
        Result<User> userResult = await CarAccessHelper.GetCurrentUserAsync(context, userContext, cancellationToken);
        if (userResult.IsFailure)
        {
            return Result.Failure(userResult.Error);
        }

        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        Result access = CarAccessHelper.ValidateCarManagement(userResult.Value, car);
        if (access.IsFailure)
        {
            return access;
        }

        if (!Enum.TryParse<CarOfferType>(command.OfferType, out CarOfferType offerType))
        {
            offerType = CarOfferType.Weekly;
        }

        if (!Enum.TryParse<CarStatus>(command.Status, out CarStatus status))
        {
            status = CarStatus.Available;
        }


        bool isAdmin = userResult.Value.Role == UserRole.Admin;

        car.Brand = command.Brand;
        car.Model = command.Model;
        car.Year = command.Year;

        // Slug-ul urmează denumirea, altfel linkul ar contrazice pagina. Cel vechi nu se păstrează:
        // intrarea prin URL-ul depășit pică pe Id și e redirectată către cel canonic.
        string slug = CarSlug.Generate(command.Brand, command.Model, command.Year, car.Id);
        if (!string.Equals(car.Slug, slug, StringComparison.Ordinal))
        {
            car.Slug = slug;
        }

        car.Engine = command.Engine;
        car.Transmission = command.Transmission;
        car.Location = command.Location;
        car.PricePerWeek = command.PricePerWeek;
        car.OldPrice = command.OldPrice;
        car.DiscountActive = command.DiscountActive;
        car.Garantie = command.Garantie;
        car.OfferType = offerType;
        car.Status = status;
        car.UberCategories = command.UberCategories;
        car.BoltCategories = command.BoltCategories;
        car.Badges = command.Badges;
        car.Description = command.Description;

        if (isAdmin)
        {
            car.Active = command.Active;
        }
        else
        {
            car.ApprovalStatus = CarApprovalStatus.Pending;
            car.Active = false;
        }

        CarListingDetailsMapper.Apply(car, command.Details);

        car.UpdatedAtUtc = DateTime.UtcNow;
        await scoreService.RecalculateAsync(car, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
