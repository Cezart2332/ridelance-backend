using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
using Application.Cars.Queries.GetAllCars;
using SharedKernel;

namespace Application.Cars.Queries.GetCarById;

/// <param name="ViewerUserId">Cine cere pagina, dacă e autentificat: proprietarul vede numărul ascuns.</param>
public sealed record GetCarByIdQuery(Guid CarId, Guid? ViewerUserId = null) : IQuery<CarDto>;

internal sealed class GetCarByIdQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetCarByIdQuery, CarDto>
{
    public Task<Result<CarDto>> Handle(GetCarByIdQuery query, CancellationToken cancellationToken) =>
        CarDetailLoader.LoadAsync(context, c => c.Id == query.CarId, cancellationToken, query.ViewerUserId);
}
