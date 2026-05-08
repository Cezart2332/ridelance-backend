using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Queries.GetAllCars;

internal sealed class GetAllCarsQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetAllCarsQuery, List<CarDto>>
{
    public async Task<Result<List<CarDto>>> Handle(GetAllCarsQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Domain.Cars.Car> queryable = context.Cars
            .AsNoTracking()
            .Include(c => c.Images.OrderBy(i => i.DisplayOrder))
            .Include(c => c.Leads);

        if (!query.AdminMode)
        {
            queryable = queryable.Where(c => c.Active);
        }

        List<Domain.Cars.Car> cars = await queryable
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var dtos = cars.Select(c => new CarDto(
            c.Id,
            c.Brand,
            c.Model,
            c.Year,
            c.Engine,
            c.Transmission,
            c.Location,
            c.PricePerWeek,
            c.OldPrice,
            c.DiscountActive,
            c.OfferType.ToString(),
            c.Status.ToString(),
            c.UberCategories,
            c.BoltCategories,
            c.Badges,
            c.Description,
            c.Active,
            c.Images.OrderBy(i => i.DisplayOrder)
                .Select(i => new CarImageDto(i.Id, i.Url, i.DisplayOrder))
                .ToList(),
            c.CreatedAtUtc,
            new CarStatsDto(
                c.Leads.Count * 3,
                c.Leads.Count,
                c.Leads.Count)
        )).ToList();

        return dtos;
    }
}
