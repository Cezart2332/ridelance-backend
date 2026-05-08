using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
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
    string OfferType,
    string Status,
    List<string> UberCategories,
    List<string> BoltCategories,
    List<string> Badges,
    string Description,
    bool Active) : ICommand;

internal sealed class UpdateCarCommandHandler(IApplicationDbContext context)
    : ICommandHandler<UpdateCarCommand>
{
    public async Task<Result> Handle(UpdateCarCommand command, CancellationToken cancellationToken)
    {
        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        if (!Enum.TryParse<CarOfferType>(command.OfferType, out CarOfferType offerType))
        {
            offerType = CarOfferType.Weekly;
        }

        if (!Enum.TryParse<CarStatus>(command.Status, out CarStatus status))
        {
            status = CarStatus.Available;
        }

        car.Brand = command.Brand;
        car.Model = command.Model;
        car.Year = command.Year;
        car.Engine = command.Engine;
        car.Transmission = command.Transmission;
        car.Location = command.Location;
        car.PricePerWeek = command.PricePerWeek;
        car.OldPrice = command.OldPrice;
        car.DiscountActive = command.DiscountActive;
        car.OfferType = offerType;
        car.Status = status;
        car.UberCategories = command.UberCategories;
        car.BoltCategories = command.BoltCategories;
        car.Badges = command.Badges;
        car.Description = command.Description;
        car.Active = command.Active;
        car.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
