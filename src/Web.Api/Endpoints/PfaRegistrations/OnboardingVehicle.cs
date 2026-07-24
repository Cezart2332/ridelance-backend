using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.Vehicle;
using Domain.PfaRegistrations;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>Pasul 5 — vehicul, copie conformă și ecusoane + dosarul PDF.</summary>
internal sealed class OnboardingVehicle : IEndpoint
{
    public sealed record SubmitVehicleRequest(
        string? OwnershipMode,
        bool AddLater,
        string? PlateNumber,
        string? Vin,
        string? Make,
        string? Model,
        int? FirstRegistrationYear,
        Guid? MarketplaceCarId);

    public sealed record BadgeRequest(string Provider, int SetCount);

    public sealed record SubmitCopyRequest(int Years, IReadOnlyList<BadgeRequest>? Badges);

    public sealed record RecordCopyConformaRequest(
        Guid? CopyConformaDocumentId,
        string? CopyConformaNumber,
        DateOnly? IssuedOn,
        DateOnly? ExpiresOn,
        string? AdminNote);

    public sealed record AdvanceBadgeRequest(string Provider, string Status, Guid? BadgeDocumentId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("onboarding/vehicle", async (
            IUserContext userContext,
            IQueryHandler<GetVehicleStateQuery, VehicleStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<VehicleStateResponse> result =
                await handler.Handle(new GetVehicleStateQuery(userContext.UserId), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("onboarding/vehicle", async (
            SubmitVehicleRequest request,
            IUserContext userContext,
            ICommandHandler<SubmitVehicleCommand, VehicleStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            VehicleOwnershipMode mode = VehicleOwnershipMode.Owned;
            if (!string.IsNullOrWhiteSpace(request.OwnershipMode) &&
                (!Enum.TryParse(request.OwnershipMode, ignoreCase: true, out mode) || !Enum.IsDefined(mode)))
            {
                return Results.BadRequest("Invalid ownership mode.");
            }

            Result<VehicleStateResponse> result = await handler.Handle(
                new SubmitVehicleCommand(
                    userContext.UserId, mode, request.AddLater, request.PlateNumber,
                    request.Vin, request.Make, request.Model, request.FirstRegistrationYear,
                    request.MarketplaceCarId),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("onboarding/vehicle/copy-request", async (
            SubmitCopyRequest request,
            IUserContext userContext,
            ICommandHandler<SubmitCopyRequestCommand, VehicleStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var badges = new List<BadgeSelection>();
            foreach (BadgeRequest badge in request.Badges ?? [])
            {
                if (!Enum.TryParse(badge.Provider, ignoreCase: true, out PfaPlatformProvider provider) ||
                    !Enum.IsDefined(provider))
                {
                    return Results.BadRequest("Invalid platform provider.");
                }

                badges.Add(new BadgeSelection(provider, badge.SetCount));
            }

            Result<VehicleStateResponse> result = await handler.Handle(
                new SubmitCopyRequestCommand(userContext.UserId, request.Years, badges),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("onboarding/vehicle/dossier", async (
            IUserContext userContext,
            ICommandHandler<GenerateVehicleDossierCommand, VehicleStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<VehicleStateResponse> result = await handler.Handle(
                new GenerateVehicleDossierCommand(userContext.UserId), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("onboarding/vehicle/submitted", async (
            IUserContext userContext,
            ICommandHandler<MarkCopyDossierSubmittedCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(
                new MarkCopyDossierSubmittedCommand(userContext.UserId), cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Admin — copia conformă emisă
        app.MapPut("pfa-registrations/{id:guid}/vehicle/copy-conforma", async (
            Guid id,
            RecordCopyConformaRequest request,
            ICommandHandler<RecordCopyConformaCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RecordCopyConformaCommand(
                id,
                request.CopyConformaDocumentId,
                request.CopyConformaNumber,
                request.IssuedOn,
                request.ExpiresOn,
                request.AdminNote);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);

        // Admin — avans manual ecusoane
        app.MapPut("pfa-registrations/{id:guid}/vehicle/badges/advance", async (
            Guid id,
            AdvanceBadgeRequest request,
            ICommandHandler<AdvanceBadgeCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.Provider, ignoreCase: true, out PfaPlatformProvider provider) || !Enum.IsDefined(provider) ||
                !Enum.TryParse(request.Status, ignoreCase: true, out VehicleBadgeStatus status) || !Enum.IsDefined(status))
            {
                return Results.BadRequest("Invalid badge values.");
            }

            Result result = await handler.Handle(
                new AdvanceBadgeCommand(id, provider, status, request.BadgeDocumentId), cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);
    }
}
