using Application.Abstractions.Messaging;
using Application.Cars.Commands.ScanRegistration;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Cars;

/// <summary>
/// Citește talonul pentru precompletarea numărului de înmatriculare și a VIN-ului.
///
/// Nu e o încărcare de document: fișierul nu se salvează. De aceea nu stă sub `documents/`, ci
/// lângă restul operațiilor pe mașini, și nu cere un `carId` — se folosește și la adăugare, când
/// mașina încă nu există.
/// </summary>
internal sealed class ScanRegistration : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cars/scan-registration", async (
            IFormFile file,
            ICommandHandler<ScanVehicleRegistrationCommand, VehicleRegistrationScan> handler,
            CancellationToken cancellationToken) =>
        {
            await using Stream stream = file.OpenReadStream();

            Result<VehicleRegistrationScan> result = await handler.Handle(
                new ScanVehicleRegistrationCommand(file.FileName, stream, file.ContentType, file.Length),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .DisableAntiforgery()
        .WithTags(Tags.Cars);
    }
}
