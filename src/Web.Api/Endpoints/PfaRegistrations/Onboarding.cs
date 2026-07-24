using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding;
using Application.PfaRegistrations.Onboarding.Eligibility;
using Application.PfaRegistrations.Onboarding.GetState;
using Application.PfaRegistrations.Onboarding.PartnerLead;
using Application.PfaRegistrations.Onboarding.RejectSection;
using Application.PfaRegistrations.Onboarding.SubmitSection;
using Application.PfaRegistrations.Onboarding.TestSkip;
using Application.PfaRegistrations.Onboarding.ValidateSection;
using Domain.PfaRegistrations;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

internal sealed class Onboarding : IEndpoint
{
    public sealed record RejectRequest(string Note);

    public sealed record EligibilityRequest(
        DateOnly? DateOfBirth,
        string? IdSeriesMask,
        DateOnly? CategoryBObtainedOn,
        string? DrivingCategories,
        DateOnly? DrivingLicenceExpiresOn,
        bool HasDriverCertificate,
        DateOnly? DriverCertificateExpiresOn);

    public sealed record PartnerLeadRequest(
        string? Phone,
        string? Email,
        string? County,
        string? HousingType,
        bool DataSharingConsent);

    public sealed record PartnerLeadStatusRequest(string Status, string? AdminNote);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Pasul 0 — starea de eligibilitate a userului curent
        app.MapGet("onboarding/eligibility", async (
            IUserContext userContext,
            IQueryHandler<GetEligibilityProfileQuery, EligibilityProfileResponse?> handler,
            CancellationToken cancellationToken) =>
        {
            Result<EligibilityProfileResponse?> result =
                await handler.Handle(new GetEligibilityProfileQuery(userContext.UserId), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Pasul 0 — trimite/actualizează datele de eligibilitate (evaluate imediat)
        app.MapPost("onboarding/eligibility", async (
            EligibilityRequest request,
            IUserContext userContext,
            ICommandHandler<SubmitEligibilityProfileCommand, EligibilityProfileResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new SubmitEligibilityProfileCommand(
                userContext.UserId,
                request.DateOfBirth,
                request.IdSeriesMask,
                request.CategoryBObtainedOn,
                request.DrivingCategories,
                request.DrivingLicenceExpiresOn,
                request.HasDriverCertificate,
                request.DriverCertificateExpiresOn);

            Result<EligibilityProfileResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Pasul 1 (Nu am PFA) — clientul trimite cererea către partenerul de înființare
        app.MapPost("onboarding/pfa/partner-lead", async (
            PartnerLeadRequest request,
            IUserContext userContext,
            ICommandHandler<SubmitPfaPartnerLeadCommand, PfaPartnerLeadResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new SubmitPfaPartnerLeadCommand(
                userContext.UserId,
                request.Phone,
                request.Email,
                request.County,
                request.HousingType,
                request.DataSharingConsent);

            Result<PfaPartnerLeadResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Pasul 1 — adminul avansează manual statusul lead-ului către partener
        app.MapPut("pfa-registrations/{id:guid}/partner-lead", async (
            Guid id,
            PartnerLeadStatusRequest request,
            ICommandHandler<UpdatePfaPartnerLeadStatusCommand, PfaPartnerLeadResponse> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.Status, ignoreCase: true, out Domain.PfaRegistrations.PfaPartnerLeadStatus status) ||
                !Enum.IsDefined(status))
            {
                return Results.BadRequest("Invalid partner lead status.");
            }

            var command = new UpdatePfaPartnerLeadStatusCommand(id, status, request.AdminNote);

            Result<PfaPartnerLeadResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);

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

        // DOAR PENTRU TESTARE — sare peste pasul curent de onboarding.
        // Activ doar cu Onboarding:EnableTestSkip=true (Onboarding__EnableTestSkip în env).
        // De șters împreună cu SkipOnboardingStepCommand și butonul din OnboardingHubPage.
        app.MapPost("onboarding/test/skip", async (
            IUserContext userContext,
            IConfiguration configuration,
            ICommandHandler<SkipOnboardingStepCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!configuration.GetValue<bool>("Onboarding:EnableTestSkip"))
            {
                return Results.NotFound();
            }

            var command = new SkipOnboardingStepCommand(userContext.UserId);

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
