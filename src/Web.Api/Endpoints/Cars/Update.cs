using Application.Abstractions.Messaging;
using Application.Cars.Commands.UpdateCar;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Infrastructure;
using Infrastructure.Authorization;

namespace Web.Api.Endpoints.Cars;

internal sealed class Update : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("cars/{id:guid}", async (
            Guid id,
            [FromBody] UpdateCarRequest request,
            ICommandHandler<UpdateCarCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateCarCommand(
                id, request.Brand, request.Model, request.Year,
                request.Engine, request.Transmission, request.Location,
                request.PricePerWeek, request.OldPrice, request.DiscountActive,
                request.OfferType, request.Status,
                request.UberCategories, request.BoltCategories, request.Badges,
                request.Description, request.Active);

            Result result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization(Permissions.ManageCars)
        .WithTags(Tags.Cars);
    }
}

internal sealed record UpdateCarRequest(
    string Brand, string Model, int Year,
    string Engine, string Transmission, string Location,
    decimal PricePerWeek, decimal? OldPrice, bool DiscountActive,
    string OfferType, string Status,
    List<string> UberCategories, List<string> BoltCategories,
    List<string> Badges, string Description, bool Active);
