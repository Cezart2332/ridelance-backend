using Application.Abstractions.Messaging;
using Application.Cars;
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
                request.PricePerWeek, request.OldPrice, request.DiscountActive, request.Garantie,
                request.OfferType, request.Status,
                request.UberCategories, request.BoltCategories, request.Badges,
                request.Description, request.Active, request.ListingSource,
                request.Details);

            Result result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Cars);
    }
}

internal sealed record UpdateCarRequest(
    string Brand, string Model, int Year,
    string Engine, string Transmission, string Location,
    decimal PricePerWeek, decimal? OldPrice, bool DiscountActive, decimal? Garantie,
    string OfferType, string Status,
    List<string> UberCategories, List<string> BoltCategories,
    List<string> Badges, string Description, bool Active,
    string ListingSource = "Ridelance",
    // Pinul de pe hartă, zona, dosarul vehiculului. Lipsea din request, deci se pierdea tăcut la
    // deserializare: comanda primea `null`, iar maparea se oprea din prima linie. Fiecare salvare
    // ștergea coordonatele, plus culoarea, locurile, numărul, VIN-ul și kilometrajul.
    CarListingDetails? Details = null);
