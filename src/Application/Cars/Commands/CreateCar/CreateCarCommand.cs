using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
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
    string OfferType,
    string Status,
    List<string> UberCategories,
    List<string> BoltCategories,
    List<string> Badges,
    string Description,
    bool Active) : ICommand<Guid>;

internal sealed class CreateCarCommandHandler(IApplicationDbContext context)
    : ICommandHandler<CreateCarCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCarCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CarOfferType>(command.OfferType, out CarOfferType offerType))
        {
            offerType = CarOfferType.Weekly;
        }

        if (!Enum.TryParse<CarStatus>(command.Status, out CarStatus status))
        {
            status = CarStatus.Available;
        }

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
            OfferType = offerType,
            Status = status,
            UberCategories = command.UberCategories,
            BoltCategories = command.BoltCategories,
            Badges = command.Badges,
            Description = command.Description,
            Active = command.Active,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.Cars.Add(car);
        await context.SaveChangesAsync(cancellationToken);

        return car.Id;
    }
}
