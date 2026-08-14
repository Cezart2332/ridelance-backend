using System.Linq.Expressions;
using Application.Abstractions.Data;
using Application.Cars.Queries.GetAllCars;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars;

/// <summary>
/// Un anunț, cu tot ce ține de el, indiferent dacă a fost cerut după Id sau după slug.
///
/// Cele două căi trebuie să returneze exact același DTO: pagina publică e deschisă din ambele
/// (link canonic vs. link vechi), iar dacă ar diverge, redirectul ar schimba și conținutul.
/// </summary>
internal static class CarDetailLoader
{
    public static async Task<Result<CarDto>> LoadAsync(
        IApplicationDbContext context,
        Expression<Func<Car, bool>> predicate,
        CancellationToken cancellationToken)
    {
        Car? car = await context.Cars
            .AsNoTracking()
            .Include(c => c.Images.OrderBy(i => i.DisplayOrder))
            .Include(c => c.Leads)
            .FirstOrDefaultAsync(predicate, cancellationToken);

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

        DateTime since = DateTime.UtcNow.AddDays(-7);
        int viewsLast7Days = await context.CarViews
            .AsNoTracking()
            .CountAsync(v => v.CarId == car.Id && v.CreatedAtUtc >= since, cancellationToken);

        return CarDtoMapper.ToDto(car, postedByAdmin, viewsLast7Days);
    }
}
