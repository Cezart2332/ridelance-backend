using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.Arr;
using Domain.PfaRegistrations;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>Pasul 3 — autorizația de transport alternativ la ARR + dosarul PDF.</summary>
internal sealed class OnboardingArr : IEndpoint
{
    public sealed record SubmitRequest(string? AgencyName, string? SubmissionMethod);

    public sealed record RecordAuthorizationRequest(
        Guid? AuthorizationDocumentId,
        string? AuthorizationNumber,
        DateOnly? AuthorizationIssuedOn,
        DateOnly? AuthorizationExpiresOn,
        string? AdminNote);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("onboarding/arr", async (
            IUserContext userContext,
            IQueryHandler<GetArrStateQuery, ArrStateResponse?> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ArrStateResponse?> result =
                await handler.Handle(new GetArrStateQuery(userContext.UserId), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Conturile de trezorerie ale agențiilor ARR. Aceeași listă pe toate ramurile —
        // cardul de plată se randează identic, altfel fix-urile prind doar o ramură.
        app.MapGet("onboarding/arr/accounts", async (
            IQueryHandler<GetArrAccountsQuery, IReadOnlyList<ArrAccountResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<ArrAccountResponse>> result =
                await handler.Handle(new GetArrAccountsQuery(), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("onboarding/arr", async (
            SubmitRequest request,
            IUserContext userContext,
            ICommandHandler<SubmitArrRequestCommand, ArrStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            ArrSubmissionMethod method = ArrSubmissionMethod.InPersonByClient;
            if (!string.IsNullOrWhiteSpace(request.SubmissionMethod) &&
                (!Enum.TryParse(request.SubmissionMethod, ignoreCase: true, out method) || !Enum.IsDefined(method)))
            {
                return Results.BadRequest("Invalid submission method.");
            }

            Result<ArrStateResponse> result = await handler.Handle(
                new SubmitArrRequestCommand(userContext.UserId, request.AgencyName, method), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("onboarding/arr/dossier", async (
            IUserContext userContext,
            ICommandHandler<GenerateArrDossierCommand, ArrStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ArrStateResponse> result = await handler.Handle(
                new GenerateArrDossierCommand(userContext.UserId), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("onboarding/arr/submitted", async (
            IUserContext userContext,
            ICommandHandler<MarkArrDossierSubmittedCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(
                new MarkArrDossierSubmittedCommand(userContext.UserId), cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Admin — înregistrează autorizația emisă
        app.MapPut("pfa-registrations/{id:guid}/arr/authorization", async (
            Guid id,
            RecordAuthorizationRequest request,
            ICommandHandler<RecordArrAuthorizationCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RecordArrAuthorizationCommand(
                id,
                request.AuthorizationDocumentId,
                request.AuthorizationNumber,
                request.AuthorizationIssuedOn,
                request.AuthorizationExpiresOn,
                request.AdminNote);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);
    }
}
