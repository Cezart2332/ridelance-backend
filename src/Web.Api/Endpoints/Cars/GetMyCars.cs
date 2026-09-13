using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
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

/// <summary>Câte anunțuri active mai are flota în abonament.</summary>
internal sealed class GetMyListingQuota : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cars/mine/listing-quota", async (
            IUserContext userContext,
            IApplicationDbContext context,
            CancellationToken cancellationToken) =>
        {
            ListingQuotaDto quota = await ListingQuota.GetAsync(context, userContext.UserId, cancellationToken);
            return Results.Ok(quota);
        })
        .RequireAuthorization(Permissions.ManageOwnCars)
        .WithTags(Tags.Cars);
    }
}
