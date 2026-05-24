using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
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

        return CarDtoMapper.ToDto(car, postedByAdmin);
    }
}
