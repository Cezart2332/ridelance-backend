using Application.Abstractions.Messaging;
using Application.Cars.Commands.CreateCar;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Infrastructure;
using Infrastructure.Authorization;

namespace Web.Api.Endpoints.Cars;

internal sealed class Create : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cars", async (
            [FromBody] CreateCarRequest request,
            ICommandHandler<CreateCarCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateCarCommand(
                request.Brand, request.Model, request.Year,
                request.Engine, request.Transmission, request.Location,
                request.PricePerWeek, request.OldPrice, request.DiscountActive,
                request.OfferType, request.Status,
                request.UberCategories, request.BoltCategories, request.Badges,
                request.Description, request.Active);

            Result<Guid> result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Created($"/cars/{result.Value}", new { id = result.Value });
        })
        .RequireAuthorization(Permissions.ManageCars)
        .WithTags(Tags.Cars);
    }
}

internal sealed record CreateCarRequest(
    string Brand, string Model, int Year,
    string Engine, string Transmission, string Location,
    decimal PricePerWeek, decimal? OldPrice, bool DiscountActive,
    string OfferType, string Status,
    List<string> UberCategories, List<string> BoltCategories,
    List<string> Badges, string Description, bool Active);
