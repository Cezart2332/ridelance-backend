using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Queries.GetAllCars;

internal sealed class GetAllCarsQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetAllCarsQuery, List<CarDto>>
{
    public async Task<Result<List<CarDto>>> Handle(GetAllCarsQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Car> queryable = context.Cars
            .AsNoTracking()
            .Include(c => c.Images.OrderBy(i => i.DisplayOrder))
            .Include(c => c.Leads);

        if (query.PosterUserId.HasValue)
        {
            queryable = queryable.Where(c => c.PostedByUserId == query.PosterUserId.Value);
        }
        else if (!query.AdminMode)
        {
            queryable = queryable.Where(c => c.Active && c.ApprovalStatus == CarApprovalStatus.Approved);
        }

        List<Car> cars = await queryable
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var posterIds = cars
            .Where(c => c.PostedByUserId.HasValue)
            .Select(c => c.PostedByUserId!.Value)
            .ToHashSet();

        Dictionary<Guid, UserRole> posterRoles = posterIds.Count == 0
            ? []
            : await context.Users
                .AsNoTracking()
                .Where(u => posterIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Role, cancellationToken);

        // O singură grupare pentru toată lista: un `count` per mașină ar fi zeci de query-uri.
        var carIds = cars.Select(c => c.Id).ToList();
        DateTime since = DateTime.UtcNow.AddDays(-7);

        Dictionary<Guid, int> recentViews = carIds.Count == 0
            ? []
            : await context.CarViews
                .AsNoTracking()
                .Where(v => carIds.Contains(v.CarId) && v.CreatedAtUtc >= since)
                .GroupBy(v => v.CarId)
                .Select(g => new { CarId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CarId, x => x.Count, cancellationToken);

        var dtos = cars
            .Select(c => CarDtoMapper.ToDto(
                c,
                CarDtoMapper.IsPostedByAdmin(c, posterRoles),
                recentViews.GetValueOrDefault(c.Id)))
            .ToList();

        return dtos;
    }
}
