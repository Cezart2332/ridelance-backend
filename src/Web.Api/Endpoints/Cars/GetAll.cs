using Application.Abstractions.Messaging;
using Application.Cars.Queries.GetAllCars;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Cars;

internal sealed class GetAll : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cars", async (
            string? sort,
            IQueryHandler<GetAllCarsQuery, List<CarDto>> handler,
            CancellationToken cancellationToken) =>
        {
            // Fără `?sort=`, lista se deschide pe „Recomandate" (spec §5.1).
            Result<List<CarDto>> result = await handler.Handle(
                new GetAllCarsQuery(AdminMode: false, Sort: sort),
                cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .AllowAnonymous()
        .WithTags(Tags.Cars);
    }
}
