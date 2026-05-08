using Application.Abstractions.Messaging;
using Application.Cars.Queries.GetAllCars;
using SharedKernel;
using Web.Api.Infrastructure;
using Infrastructure.Authorization;

namespace Web.Api.Endpoints.Cars;

internal sealed class GetAllAdmin : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cars/admin", async (
            IQueryHandler<GetAllCarsQuery, List<CarDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<CarDto>> result = await handler.Handle(new GetAllCarsQuery(AdminMode: true), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization(Permissions.ManageCars)
        .WithTags(Tags.Cars);
    }
}
