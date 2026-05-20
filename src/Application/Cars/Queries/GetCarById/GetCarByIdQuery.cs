using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars.Queries.GetAllCars;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Queries.GetCarById;

public sealed record GetCarByIdQuery(Guid CarId) : IQuery<CarDto>;

internal sealed class GetCarByIdQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetCarByIdQuery, CarDto>
{
    public async Task<Result<CarDto>> Handle(GetCarByIdQuery query, CancellationToken cancellationToken)
    {
        Car? car = await context.Cars
            .AsNoTracking()
            .Include(c => c.Images.OrderBy(i => i.DisplayOrder))
            .Include(c => c.Leads)
            .FirstOrDefaultAsync(c => c.Id == query.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<CarDto>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        bool postedByAdmin = true;
        if (car.PostedByUserId.HasValue)
        {
            UserRole? role = await context.Users
                .AsNoTracking()
                .Where(u => u.Id == car.PostedByUserId.Value)
                .Select(u => (UserRole?)u.Role)
                .FirstOrDefaultAsync(cancellationToken);
            postedByAdmin = role == UserRole.Admin;
        }

        return new CarDto(
            car.Id, car.Brand, car.Model, car.Year,
            car.Engine, car.Transmission, car.Location,
            car.PricePerWeek, car.OldPrice, car.DiscountActive, car.Garantie,
            car.OfferType.ToString(), car.Status.ToString(),
            car.UberCategories, car.BoltCategories, car.Badges,
            car.Description, car.Active,
            car.ListingSource.ToString(),
            car.ApprovalStatus.ToString(),
            postedByAdmin,
            car.Images.OrderBy(i => i.DisplayOrder)
                .Select(i => new CarImageDto(i.Id, i.Url, i.DisplayOrder))
                .ToList(),
            car.CreatedAtUtc,
            new CarStatsDto(car.Leads.Count * 3, car.Leads.Count, car.Leads.Count));
    }
}
