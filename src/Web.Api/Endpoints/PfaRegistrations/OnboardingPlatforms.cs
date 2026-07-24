using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.Platforms;
using Domain.PfaRegistrations;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>Pasul 4 — conturi operator Uber & Bolt.</summary>
internal sealed class OnboardingPlatforms : IEndpoint
{
    public sealed record SelectRequest(bool UberSelected, bool BoltSelected);

    public sealed record AccountRequest(
        string Provider,
        bool HasExistingAccount,
        string? OperatorAccountId,
        Guid? AffiliationContractDocumentId);

    public sealed record AdvanceRequest(string Provider, string OnboardingStatus);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("onboarding/platforms", async (
            IUserContext userContext,
            IQueryHandler<GetPlatformOnboardingQuery, PlatformOnboardingResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<PlatformOnboardingResponse> result =
                await handler.Handle(new GetPlatformOnboardingQuery(userContext.UserId), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("onboarding/platforms/select", async (
            SelectRequest request,
            IUserContext userContext,
            ICommandHandler<SelectPlatformsCommand, PlatformOnboardingResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<PlatformOnboardingResponse> result = await handler.Handle(
                new SelectPlatformsCommand(userContext.UserId, request.UberSelected, request.BoltSelected),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("onboarding/platforms/account", async (
            AccountRequest request,
            IUserContext userContext,
            ICommandHandler<SubmitPlatformAccountCommand, PlatformOnboardingResponse> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.Provider, ignoreCase: true, out PfaPlatformProvider provider) || !Enum.IsDefined(provider))
            {
                return Results.BadRequest("Invalid platform provider.");
            }

            Result<PlatformOnboardingResponse> result = await handler.Handle(
                new SubmitPlatformAccountCommand(
                    userContext.UserId, provider, request.HasExistingAccount,
                    request.OperatorAccountId, request.AffiliationContractDocumentId),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Admin — avans manual
        app.MapPut("pfa-registrations/{id:guid}/platforms/advance", async (
            Guid id,
            AdvanceRequest request,
            ICommandHandler<AdvancePlatformOnboardingCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.Provider, ignoreCase: true, out PfaPlatformProvider provider) || !Enum.IsDefined(provider) ||
                !Enum.TryParse(request.OnboardingStatus, ignoreCase: true, out PfaPlatformOnboardingStatus status) || !Enum.IsDefined(status))
            {
                return Results.BadRequest("Invalid platform values.");
            }

            Result result = await handler.Handle(
                new AdvancePlatformOnboardingCommand(id, provider, status), cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);
    }
}
