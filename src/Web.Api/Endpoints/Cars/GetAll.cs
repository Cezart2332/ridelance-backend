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
            IQueryHandler<GetAllCarsQuery, List<CarDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<CarDto>> result = await handler.Handle(new GetAllCarsQuery(AdminMode: false), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .AllowAnonymous()
        .WithTags(Tags.Cars);
    }
}
