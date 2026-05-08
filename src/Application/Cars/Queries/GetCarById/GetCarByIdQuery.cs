using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars.Queries.GetAllCars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Queries.GetCarById;

public sealed record GetCarByIdQuery(Guid CarId) : IQuery<CarDto>;

internal sealed class GetCarByIdQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetCarByIdQuery, CarDto>
{
    public async Task<Result<CarDto>> Handle(GetCarByIdQuery query, CancellationToken cancellationToken)
    {
        Domain.Cars.Car? car = await context.Cars
            .AsNoTracking()
            .Include(c => c.Images.OrderBy(i => i.DisplayOrder))
            .Include(c => c.Leads)
            .FirstOrDefaultAsync(c => c.Id == query.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<CarDto>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        return new CarDto(
            car.Id, car.Brand, car.Model, car.Year,
            car.Engine, car.Transmission, car.Location,
            car.PricePerWeek, car.OldPrice, car.DiscountActive,
            car.OfferType.ToString(), car.Status.ToString(),
            car.UberCategories, car.BoltCategories, car.Badges,
            car.Description, car.Active,
            car.Images.OrderBy(i => i.DisplayOrder)
                .Select(i => new CarImageDto(i.Id, i.Url, i.DisplayOrder))
                .ToList(),
            car.CreatedAtUtc,
            new CarStatsDto(car.Leads.Count * 3, car.Leads.Count, car.Leads.Count));
    }
}
