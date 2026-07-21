using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding;
using Application.PfaRegistrations.Onboarding.GetState;
using Application.PfaRegistrations.Onboarding.RejectSection;
using Application.PfaRegistrations.Onboarding.SubmitSection;
using Application.PfaRegistrations.Onboarding.ValidateSection;
using Domain.PfaRegistrations;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

internal sealed class Onboarding : IEndpoint
{
    public sealed record RejectRequest(string Note);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Starea de onboarding a userului curent (client)
        app.MapGet("onboarding/state", async (
            IUserContext userContext,
            IQueryHandler<GetOnboardingStateQuery, OnboardingStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetOnboardingStateQuery(userContext.UserId);

            Result<OnboardingStateResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Clientul trimite o secțiune la validare
        app.MapPost("onboarding/sections/{key}/submit", async (
            string key,
            IUserContext userContext,
            ICommandHandler<SubmitOnboardingSectionCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseSectionKey(key, out OnboardingSectionKey sectionKey))
            {
                return Results.BadRequest("Invalid section key.");
            }

            var command = new SubmitOnboardingSectionCommand(userContext.UserId, sectionKey);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Starea de onboarding a unui dosar (admin/contabil)
        app.MapGet("pfa-registrations/{id:guid}/onboarding", async (
            Guid id,
            IQueryHandler<GetOnboardingStateForRegistrationQuery, OnboardingStateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetOnboardingStateForRegistrationQuery(id);

            Result<OnboardingStateResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:view")
        .WithTags(Tags.PfaRegistrations);

        // Adminul validează o secțiune
        app.MapPut("pfa-registrations/{id:guid}/sections/{key}/validate", async (
            Guid id,
            string key,
            IUserContext userContext,
            ICommandHandler<ValidateOnboardingSectionCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseSectionKey(key, out OnboardingSectionKey sectionKey))
            {
                return Results.BadRequest("Invalid section key.");
            }

            var command = new ValidateOnboardingSectionCommand(id, sectionKey, userContext.UserId);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);

        // Adminul respinge o secțiune (cu motiv obligatoriu)
        app.MapPut("pfa-registrations/{id:guid}/sections/{key}/reject", async (
            Guid id,
            string key,
            RejectRequest request,
            IUserContext userContext,
            ICommandHandler<RejectOnboardingSectionCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseSectionKey(key, out OnboardingSectionKey sectionKey))
            {
                return Results.BadRequest("Invalid section key.");
            }

            var command = new RejectOnboardingSectionCommand(id, sectionKey, userContext.UserId, request.Note);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);
    }

    private static bool TryParseSectionKey(string raw, out OnboardingSectionKey key)
    {
        // Doar nume de secțiuni (nu valori numerice gen "2")
        return Enum.TryParse(raw, ignoreCase: true, out key) &&
               !char.IsDigit(raw[0]) &&
               Enum.IsDefined(key);
    }
}
