using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.ArrFleet;
using Domain.PfaRegistrations.ArrFleet;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>
/// Pasul „ARR &amp; Cont Flotă”. Clientul salvează draftul și trimite; documentele lui trec prin
/// încărcarea obișnuită (<c>documents/upload</c>), care validează deja tipul și mărimea. Adminul
/// citește aceleași date, avansează statusul, încarcă documentele oficiale și redeschide pasul.
/// </summary>
internal sealed class OnboardingArrFleet : IEndpoint
{
    public sealed record DraftRequest(
        IReadOnlyList<string>? Platforms,
        IReadOnlyList<ArrFleetDriverAccountInput>? DriverAccounts,
        string? VehicleOwnership);

    public sealed record StatusRequest(string Status);

    public sealed record ReopenRequest(string Reason);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("onboarding/arr-fleet", async (
            IUserContext userContext,
            IQueryHandler<GetArrFleetStateQuery, ArrFleetStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ArrFleetStateResponse> result = await handler.Handle(new GetArrFleetStateQuery(userContext.UserId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Conturile de trezorerie ale agențiilor ARR, pe județe: sursa unică, pentru oricine are cont.
        app.MapGet("onboarding/arr-fleet/accounts", async (
            IQueryHandler<GetArrAccountsQuery, IReadOnlyList<ArrAccountResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<ArrAccountResponse>> result = await handler.Handle(new GetArrAccountsQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPut("onboarding/arr-fleet", async (
            DraftRequest request,
            IUserContext userContext,
            ICommandHandler<SaveArrFleetDraftCommand, ArrFleetStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ArrFleetStateResponse> result = await handler.Handle(
                new SaveArrFleetDraftCommand(userContext.UserId, request.Platforms, request.DriverAccounts, request.VehicleOwnership),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("onboarding/arr-fleet/submit", async (
            IUserContext userContext,
            ICommandHandler<SubmitArrFleetCommand, ArrFleetStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ArrFleetStateResponse> result = await handler.Handle(new SubmitArrFleetCommand(userContext.UserId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapGet("admin/onboarding/{id:guid}/arr-fleet", async (
            Guid id,
            IQueryHandler<GetAdminArrFleetQuery, ArrFleetStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ArrFleetStateResponse> result = await handler.Handle(new GetAdminArrFleetQuery(id), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);

        app.MapPatch("admin/onboarding/{id:guid}/arr-fleet/status", async (
            Guid id,
            StatusRequest request,
            IUserContext userContext,
            ICommandHandler<ChangeArrFleetStatusCommand, ArrFleetStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.Status, ignoreCase: true, out ArrFleetStatus status) || !Enum.IsDefined(status))
            {
                return Results.BadRequest("Status necunoscut.");
            }

            Result<ArrFleetStateResponse> result = await handler.Handle(
                new ChangeArrFleetStatusCommand(id, userContext.UserId, status), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("admin/onboarding/{id:guid}/arr-fleet/reopen", async (
            Guid id,
            ReopenRequest request,
            IUserContext userContext,
            ICommandHandler<ReopenArrFleetCommand, ArrFleetStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ArrFleetStateResponse> result = await handler.Handle(
                new ReopenArrFleetCommand(id, userContext.UserId, request.Reason), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("admin/onboarding/{id:guid}/arr-fleet/official-documents", async (
            Guid id,
            [FromForm] IFormFile file,
            [FromForm] string type,
            [FromForm] string? documentNumber,
            [FromForm] string? issuedAt,
            [FromForm] string? expiresAt,
            IUserContext userContext,
            ICommandHandler<UploadArrFleetOfficialDocumentCommand, ArrFleetStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(type, ignoreCase: true, out ArrFleetOfficialDocument documentType) || !Enum.IsDefined(documentType))
            {
                return Results.BadRequest("Tip de document necunoscut.");
            }

            using Stream stream = file.OpenReadStream();

            Result<ArrFleetStateResponse> result = await handler.Handle(
                new UploadArrFleetOfficialDocumentCommand(
                    id,
                    userContext.UserId,
                    documentType,
                    file.FileName,
                    file.ContentType,
                    stream,
                    file.Length,
                    documentNumber,
                    ParseDate(issuedAt),
                    ParseDate(expiresAt)),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .DisableAntiforgery()
        .WithTags(Tags.PfaRegistrations);
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)
            ? parsed
            : null;
}
