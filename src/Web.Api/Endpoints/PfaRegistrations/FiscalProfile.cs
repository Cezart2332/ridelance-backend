using Application.Abstractions.Messaging;
using Application.PfaRegistrations.FiscalProfile;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

internal sealed class FiscalProfile : IEndpoint
{
    public sealed record UpsertFiscalProfileRequest(
        string SpecialVatCodeStatus,
        DateTime? SpecialVatCodeObtainedAtUtc,
        Guid? SpecialVatCodeDocumentId,
        string UberStatus,
        string BoltStatus,
        string OtherPlatformsStatus,
        string CashRevenueStatus,
        string CashRegisterStatus,
        string VehicleUsageType,
        string? VehicleSupportingDocumentLabel,
        Guid? VehicleSupportingDocumentId);

    public sealed record UpsertPlatformAccountsRequest(
        IReadOnlyList<UpsertPfaPlatformAccountItem> Accounts);

    public sealed record MarkFleetConfiguredRequest(string Provider);

    public sealed record AcceptFleetConsentRequest(
        bool FleetAccountsAccepted,
        bool BoltApiAccepted);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("pfa-registrations/{id:guid}/fiscal-profile", async (
            Guid id,
            IQueryHandler<GetPfaFiscalProfileQuery, PfaFiscalSettingsResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetPfaFiscalProfileQuery(id);
            Result<PfaFiscalSettingsResponse> result = await handler.Handle(query, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPut("pfa-registrations/{id:guid}/fiscal-profile", async (
            Guid id,
            UpsertFiscalProfileRequest request,
            ICommandHandler<UpsertPfaFiscalProfileCommand, PfaFiscalProfileResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new UpsertPfaFiscalProfileCommand(
                id,
                request.SpecialVatCodeStatus,
                request.SpecialVatCodeObtainedAtUtc,
                request.SpecialVatCodeDocumentId,
                request.UberStatus,
                request.BoltStatus,
                request.OtherPlatformsStatus,
                request.CashRevenueStatus,
                request.CashRegisterStatus,
                request.VehicleUsageType,
                request.VehicleSupportingDocumentLabel,
                request.VehicleSupportingDocumentId);

            Result<PfaFiscalProfileResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.PfaRegistrations);

        app.MapPut("pfa-registrations/{id:guid}/platform-accounts", async (
            Guid id,
            UpsertPlatformAccountsRequest request,
            ICommandHandler<UpsertPfaPlatformAccountsCommand, IReadOnlyList<PfaPlatformAccountResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new UpsertPfaPlatformAccountsCommand(id, request.Accounts);
            Result<IReadOnlyList<PfaPlatformAccountResponse>> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("pfa-registrations/{id:guid}/fleet-configured", async (
            Guid id,
            MarkFleetConfiguredRequest request,
            ICommandHandler<MarkPfaFleetAccountConfiguredCommand, PfaPlatformAccountResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new MarkPfaFleetAccountConfiguredCommand(id, request.Provider);
            Result<PfaPlatformAccountResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("pfa-registrations/{id:guid}/fleet-consent", async (
            Guid id,
            AcceptFleetConsentRequest request,
            ICommandHandler<AcceptPfaFleetConsentCommand, PfaFleetConsentResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new AcceptPfaFleetConsentCommand(
                id,
                request.FleetAccountsAccepted,
                request.BoltApiAccepted);

            Result<PfaFleetConsentResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);
    }
}
