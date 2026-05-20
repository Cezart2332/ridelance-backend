using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Cars.Queries.GetAllCars;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Cars;

internal sealed class GetMyCars : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cars/mine", async (
            IUserContext userContext,
            IQueryHandler<GetAllCarsQuery, List<CarDto>> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetAllCarsQuery(AdminMode: true, PosterUserId: userContext.UserId);
            Result<List<CarDto>> result = await handler.Handle(query, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization(Permissions.ManageOwnCars)
        .WithTags(Tags.Cars);
    }
}
