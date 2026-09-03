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
        Guid? AffiliationContractDocumentId,
        string? ExistingAccountAnswer,
        string? Email,
        string? Phone,
        // Parola contului de flotă: se stochează criptată și nu se mai întoarce niciodată.
        string? Password,
        // Contul de ȘOFER de pe aceeași platformă — alt cont decât cel de flotă. ID-ul e opțional
        // și nu se mai cere în onboarding; rămâne acceptat pentru dosarele care îl au.
        string? DriverEmail,
        string? DriverPhone,
        string? DriverFullName,
        string? DriverExternalId);

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

            // „Nu știu ce tip de cont am” nu mai e un răspuns acceptat: lăsa dosarul într-o stare
            // pe care nici clientul, nici operatorul nu o puteau duce mai departe.
            if (request.ExistingAccountAnswer is not (null or "HasOperatorAccount" or "DriverOnly" or "None"))
            {
                return Results.BadRequest("Invalid existing account answer.");
            }

            Result<PlatformOnboardingResponse> result = await handler.Handle(
                new SubmitPlatformAccountCommand(
                    userContext.UserId, provider, request.HasExistingAccount,
                    request.OperatorAccountId, request.AffiliationContractDocumentId,
                    request.ExistingAccountAnswer,
                    request.Email, request.Phone, request.Password,
                    request.DriverEmail, request.DriverPhone,
                    request.DriverFullName, request.DriverExternalId),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Admin — ce a completat șoferul la pasul 5. Pasul n-are documente, deci fără endpointul
        // ăsta panoul de validare afișa un grup gol pentru date care erau salvate de mult.
        app.MapGet("pfa-registrations/{id:guid}/platforms", async (
            Guid id,
            IQueryHandler<GetPlatformOnboardingForRegistrationQuery, PlatformOnboardingResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<PlatformOnboardingResponse> result = await handler.Handle(
                new GetPlatformOnboardingForRegistrationQuery(id), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:view")
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
