using Application.Abstractions.Messaging;
using Application.Rentals.Checks;
using Microsoft.AspNetCore.Mvc;
using Domain.Rentals;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

internal sealed class Checks : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("rentals/{id:guid}/checks", async (
            Guid id,
            IQueryHandler<GetChecksQuery, ChecksDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ChecksDto> result = await handler.Handle(new GetChecksQuery(id), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);

        app.MapPut("rentals/{id:guid}/checks/{kind}", async (
            Guid id,
            string kind,
            CheckRequest body,
            ICommandHandler<SaveCheckRecordCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);

            if (!Enum.TryParse(kind, ignoreCase: true, out CheckKind parsed))
            {
                return Results.BadRequest(new { detail = "Tip necunoscut: predare sau primire." });
            }

            Result<Guid> result = await handler.Handle(
                new SaveCheckRecordCommand(
                    id,
                    parsed,
                    body.OccurredAtUtc,
                    body.Mileage,
                    body.FuelLevel,
                    body.Accessories,
                    body.Notes,
                    body.DepositReturnedBani,
                    body.DepositWithheldBani,
                    body.WithholdingReason,
                    body.ExtraMileageChargeBani,
                    body.OtherChargesBani),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(new { id = result.Value });
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }

    internal sealed record CheckRequest(
        DateTime OccurredAtUtc,
        int Mileage,
        string? FuelLevel,
        IReadOnlyList<string>? Accessories,
        string? Notes,
        long? DepositReturnedBani,
        long? DepositWithheldBani,
        string? WithholdingReason,
        long? ExtraMileageChargeBani,
        long? OtherChargesBani);
}

internal sealed class VehicleTimelineEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cars/{carId:guid}/timeline", async (
            Guid carId,
            IQueryHandler<GetVehicleTimelineQuery, List<VehicleEventDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<VehicleEventDto>> result = await handler.Handle(
                new GetVehicleTimelineQuery(carId), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Cars);
    }
}

internal sealed class CheckPhotos : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("rentals/{id:guid}/checks/{kind}/photos", async (
            Guid id,
            string kind,
            [FromForm] IFormFile file,
            [FromForm] string slot,
            ICommandHandler<AddCheckPhotoCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(file);

            if (!Enum.TryParse(kind, ignoreCase: true, out CheckKind parsedKind))
            {
                return Results.BadRequest(new { detail = "Tip necunoscut: predare sau primire." });
            }

            if (!Enum.TryParse(slot, ignoreCase: true, out CheckPhotoSlot parsedSlot))
            {
                return Results.BadRequest(new { detail = "Slot necunoscut." });
            }

            using Stream stream = file.OpenReadStream();

            Result<Guid> result = await handler.Handle(
                new AddCheckPhotoCommand(
                    id, parsedKind, parsedSlot, file.FileName, file.ContentType, stream, file.Length),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(new { id = result.Value });
        })
        .RequireAuthorization()
        .DisableAntiforgery()
        .WithTags(Tags.Companies);
    }
}
